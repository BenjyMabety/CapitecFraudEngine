using CapitecFraudEngine.Domain;
using CapitecFraudEngine.Infrastructure;
using Dapper;
using MySqlConnector;

namespace CapitecFraudEngine.Api;



public static class ApiEndpoints
{
    private static (string Username, string UserId) _activeUser = ("Anonymous", "N/A");
    public static void MapEndpoints(this WebApplication app)
    {
        var connectionString = app.Configuration.GetConnectionString("DefaultConnection")!;

        // -----------------------------------------------------------------------------
        // AUTHENTICATION ENDPOINT (SHA-256)
        // -----------------------------------------------------------------------------
        app.MapPost("/api/v1/auth/login", async (LoginRequestDto request, ILogger<Program> logger) =>
        {
            logger.LogInformation("[AUTH ATTEMPT] Login requested for Username: {UserName}", request.UserName);

            if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            {
                logger.LogWarning("[AUTH FAILED] Missing username or password in login payload.");
                return Results.BadRequest(new { Error = "Username and password are required." });
            }

            try
            {
                using var db = new MySqlConnection(connectionString);

                // 1. Hash the incoming plain text password
                var hashedPassword = SecurityUtils.HashPasswordSha256(request.Password);

                // 2. Query against stored SHA-256 hash
                var sql = @"
    SELECT 
        UserId, 
        UserName, 
        UserCreatedDate, 
        UserLastLogin 
    FROM Users 
    WHERE UserName = @UserName AND UserPassword = @HashedPassword;";

                var user = await db.QueryFirstOrDefaultAsync<UserDto>(sql, new
                {
                    UserName = request.UserName.Trim(),
                    HashedPassword = hashedPassword
                });

                if (user == null)
                {
                    logger.LogWarning("[AUTH FAILED] Invalid credentials for Username: {UserName}", request.UserName);
                    return Results.Json(
                        new { Error = "Invalid username or password." },
                        statusCode: StatusCodes.Status401Unauthorized
                    );
                }

                // 3. Record last login timestamp
                await db.ExecuteAsync("UPDATE Users SET UserLastLogin = NOW() WHERE UserId = @UserId;", new { UserId = user.UserId });
                user.UserLastLogin = DateTime.UtcNow;
                _activeUser = (user.UserName, user.UserId.ToString());

                logger.LogInformation("[AUTH SUCCESS] User logged in successfully. UserId: {UserId} | Username: {UserName} | Status: 200 OK",
                    user.UserId, user.UserName);

                return Results.Ok(new LoginResponseDto
                {
                    Message = "Authentication successful.",
                    User = user
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[AUTH ERROR] Exception occurred during login attempt for Username: {UserName}", request.UserName);
                return Results.Json(
                    new { Error = "An error occurred during authentication.", Details = ex.Message },
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        })
        .WithName("AuthenticateUser")
        .WithTags("Authentication");

        // Generator Endpoint
        app.MapPost("/api/v1/generator/sample-file", () =>
        {
            var collectorDir = Path.Combine(Directory.GetCurrentDirectory(), "data", "collector");
            var path = DummyFileGenerator.Generate(collectorDir);
            return Results.Ok(new { Message = "File generated", FilePath = path });
        })
        .WithTags("Testing Utilities");

        // -----------------------------------------------------------------------------
        // FRAUD RULES ENDPOINTS WITH LOGGING (Includes User Context)
        // -----------------------------------------------------------------------------
        app.MapGet("/api/v1/rules", async () =>
        {
            using var db = new MySqlConnection(connectionString);
            var rules = await db.QueryAsync<FraudRule>("SELECT * FROM FraudRules");
            return Results.Ok(rules);
        })
        .WithTags("Fraud Rules");

        static (string username, string userId) GetUserContext(HttpContext httpContext)
        {
            // 1. Check JWT / Identity claims
            var username = httpContext.User.FindFirst("UserName")?.Value
                ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

            var userId = httpContext.User.FindFirst("UserId")?.Value
                ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            // 2. Fall back to custom Request Headers
            username ??= httpContext.Request.Headers["X-User-Name"].FirstOrDefault()
                      ?? httpContext.Request.Headers["UserName"].FirstOrDefault();

            userId ??= httpContext.Request.Headers["X-User-Id"].FirstOrDefault()
                    ?? httpContext.Request.Headers["UserId"].FirstOrDefault();

            // 3. Fall back to active logged-in instance user
            if (string.IsNullOrEmpty(username))
            {
                return _activeUser;
            }

            return (username, userId ?? "N/A");
        }

        // POST: Add new Fraud Rule
        app.MapPost("/api/v1/rules", async (FraudRule rule, HttpContext httpContext, ILogger<Program> logger) =>
        {
            var (username, userId) = GetUserContext(httpContext);

            if (string.IsNullOrWhiteSpace(rule.RuleName) || string.IsNullOrWhiteSpace(rule.FieldName))
            {
                logger.LogWarning("[RULE CREATE FAILED] [User: {Username}|{UserId}] Missing required fields: RuleName or FieldName.", username, userId);
                return Results.BadRequest(new { Error = "RuleName and FieldName are required fields." });
            }

            try
            {
                using var db = new MySqlConnection(connectionString);
                var sql = @"INSERT INTO FraudRules (RuleName, FieldName, Operator, ThresholdValue, IsActive)
                            VALUES (@RuleName, @FieldName, @Operator, @ThresholdValue, @IsActive);
                            SELECT LAST_INSERT_ID();";

                var ruleId = await db.QuerySingleAsync<int>(sql, rule);
                rule.RuleId = ruleId;

                logger.LogInformation("[RULE CREATED] [User: {Username}|{UserId}] RuleID: {RuleId} | Name: {RuleName} | Condition: {FieldName} {Operator} {ThresholdValue} | IsActive: {IsActive}",
                    username, userId, rule.RuleId, rule.RuleName, rule.FieldName, rule.Operator, rule.ThresholdValue, rule.IsActive);

                return Results.Created($"/api/v1/rules/{ruleId}", rule);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[RULE CREATE ERROR] [User: {Username}|{UserId}] Failed to create rule: {RuleName}", username, userId, rule.RuleName);
                return Results.Json(new { Error = "An error occurred while creating the rule.", Details = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithTags("Fraud Rules");

        // PUT: Update existing Fraud Rule
        app.MapPut("/api/v1/rules/{id:int}", async (int id, FraudRule rule, HttpContext httpContext, ILogger<Program> logger) =>
        {
            var (username, userId) = GetUserContext(httpContext);

            if (string.IsNullOrWhiteSpace(rule.RuleName) || string.IsNullOrWhiteSpace(rule.FieldName))
            {
                logger.LogWarning("[RULE UPDATE FAILED] [User: {Username}|{UserId}] RuleID: {RuleId} | Missing required fields: RuleName or FieldName.", username, userId, id);
                return Results.BadRequest(new { Error = "RuleName and FieldName are required fields." });
            }

            try
            {
                using var db = new MySqlConnection(connectionString);

                var sql = @"UPDATE FraudRules 
                            SET RuleName = @RuleName, 
                                FieldName = @FieldName, 
                                Operator = @Operator, 
                                ThresholdValue = @ThresholdValue, 
                                IsActive = @IsActive 
                            WHERE RuleId = @RuleId;";

                var rowsAffected = await db.ExecuteAsync(sql, new
                {
                    RuleId = id,
                    rule.RuleName,
                    rule.FieldName,
                    rule.Operator,
                    rule.ThresholdValue,
                    rule.IsActive
                });

                if (rowsAffected == 0)
                {
                    logger.LogWarning("[RULE UPDATE FAILED] [User: {Username}|{UserId}] RuleID: {RuleId} not found.", username, userId, id);
                    return Results.NotFound(new { Error = $"Fraud rule with ID {id} was not found." });
                }

                rule.RuleId = id;

                logger.LogInformation("[RULE UPDATED] [User: {Username}|{UserId}] RuleID: {RuleId} | Name: {RuleName} | Condition: {FieldName} {Operator} {ThresholdValue} | IsActive: {IsActive}",
                    username, userId, id, rule.RuleName, rule.FieldName, rule.Operator, rule.ThresholdValue, rule.IsActive);

                return Results.Ok(rule);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[RULE UPDATE ERROR] [User: {Username}|{UserId}] Failed to update RuleID: {RuleId}", username, userId, id);
                return Results.Json(new { Error = "An error occurred while updating the rule.", Details = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("UpdateFraudRule")
        .WithTags("Fraud Rules");

        // DELETE: Remove Fraud Rule by ID
        app.MapDelete("/api/v1/rules/{id:int}", async (int id, HttpContext httpContext, ILogger<Program> logger) =>
        {
            var (username, userId) = GetUserContext(httpContext);

            try
            {
                using var db = new MySqlConnection(connectionString);

                var sql = "DELETE FROM FraudRules WHERE RuleId = @RuleId;";
                var rowsAffected = await db.ExecuteAsync(sql, new { RuleId = id });

                if (rowsAffected == 0)
                {
                    logger.LogWarning("[RULE DELETE FAILED] [User: {Username}|{UserId}] RuleID: {RuleId} not found.", username, userId, id);
                    return Results.NotFound(new { Error = $"Fraud rule with ID {id} was not found." });
                }

                logger.LogInformation("[RULE DELETED] [User: {Username}|{UserId}] Fraud rule with ID: {RuleId} successfully deleted.", username, userId, id);

                return Results.Ok(new { Message = $"Fraud rule with ID {id} was successfully deleted." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[RULE DELETE ERROR] [User: {Username}|{UserId}] Failed to delete RuleID: {RuleId}", username, userId, id);
                return Results.Json(new { Error = "An error occurred while deleting the rule.", Details = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("DeleteFraudRule")
        .WithTags("Fraud Rules");

        // Alerts Endpoint
        app.MapGet("/api/v1/alerts", async () =>
        {
            using var db = new MySqlConnection(connectionString);
            var sql = @"
                SELECT 
                    a.AlertId,
                    a.TriggeredAt,
                    a.TransactionId,
                    t.AccountNumber,
                    t.AccountName,
                    t.Amount,
                    CASE t.TransactionType
                        WHEN 1 THEN 'Deposit'
                        WHEN 2 THEN 'Withdrawal'
                        WHEN 3 THEN 'Transfer'
                        WHEN 4 THEN 'Payment'
                        ELSE 'Unknown'
                    END AS TransactionType,
                    r.RuleName AS RuleBroken,
                    CONCAT(r.FieldName, ' ', r.Operator, ' ', r.ThresholdValue) AS RuleCondition
                FROM FraudAlerts a
                JOIN Transactions t ON a.TransactionId = t.TransactionId
                JOIN FraudRules r ON a.RuleId = r.RuleId;";

            var alerts = await db.QueryAsync<FraudAlertDto>(sql);
            return Results.Ok(alerts);
        })
        .WithTags("Fraud Alerts");

        // Audit Endpoint
        app.MapGet("/api/v1/files", async () =>
        {
            using var db = new MySqlConnection(connectionString);
            var files = await db.QueryAsync<ProcessedFile>("SELECT * FROM ProcessedFiles");
            return Results.Ok(files);
        })
        .WithTags("File Audit");
    }
}