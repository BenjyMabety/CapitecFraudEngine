using CapitecFraudEngine.Domain;
using CapitecFraudEngine.Infrastructure;
using Dapper;
using MySqlConnector;

namespace CapitecFraudEngine.Api;

public static class ApiEndpoints
{
    public static void MapEndpoints(this WebApplication app)
    {
        var connectionString = app.Configuration.GetConnectionString("DefaultConnection")!;

        // -----------------------------------------------------------------------------
        // AUTHENTICATION ENDPOINT (SHA-256)
        // -----------------------------------------------------------------------------
        app.MapPost("/api/v1/auth/login", async (LoginRequestDto request) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            {
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
                    return Results.Json(
                        new { Error = "Invalid username or password." },
                        statusCode: StatusCodes.Status401Unauthorized
                    );
                }

                // 3. Record last login timestamp
                await db.ExecuteAsync("UPDATE Users SET UserLastLogin = NOW() WHERE UserId = @UserId;", new { UserId = user.UserId });
                user.UserLastLogin = DateTime.UtcNow;

                return Results.Ok(new LoginResponseDto
                {
                    Message = "Authentication successful.",
                    User = user
                });
            }
            catch (Exception ex)
            {
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

        // Rules Endpoints
        app.MapGet("/api/v1/rules", async () =>
        {
            using var db = new MySqlConnection(connectionString);
            var rules = await db.QueryAsync<FraudRule>("SELECT * FROM FraudRules");
            return Results.Ok(rules);
        })
        .WithTags("Fraud Rules");

        app.MapPost("/api/v1/rules", async (FraudRule rule) =>
        {
            using var db = new MySqlConnection(connectionString);
            var sql = @"INSERT INTO FraudRules (RuleName, FieldName, Operator, ThresholdValue, IsActive)
                        VALUES (@RuleName, @FieldName, @Operator, @ThresholdValue, @IsActive);
                        SELECT LAST_INSERT_ID();";

            var ruleId = await db.QuerySingleAsync<int>(sql, rule);
            rule.RuleId = ruleId;
            return Results.Created($"/api/v1/rules/{ruleId}", rule);
        })
        .WithTags("Fraud Rules");

        // PUT: Update an existing Fraud Rule
        app.MapPut("/api/v1/rules/{id:int}", async (int id, FraudRule rule) =>
        {
            if (string.IsNullOrWhiteSpace(rule.RuleName) || string.IsNullOrWhiteSpace(rule.FieldName))
            {
                return Results.BadRequest(new { Error = "RuleName and FieldName are required fields." });
            }

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
                return Results.NotFound(new { Error = $"Fraud rule with ID {id} was not found." });
            }

            rule.RuleId = id;
            return Results.Ok(rule);
        })
        .WithName("UpdateFraudRule")
        .WithTags("Fraud Rules");

        // DELETE: Remove a Fraud Rule by ID
        app.MapDelete("/api/v1/rules/{id:int}", async (int id) =>
        {
            using var db = new MySqlConnection(connectionString);

            var sql = "DELETE FROM FraudRules WHERE RuleId = @RuleId;";
            var rowsAffected = await db.ExecuteAsync(sql, new { RuleId = id });

            if (rowsAffected == 0)
            {
                return Results.NotFound(new { Error = $"Fraud rule with ID {id} was not found." });
            }

            return Results.Ok(new { Message = $"Fraud rule with ID {id} was successfully deleted." });
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
                    t.TransactionType,
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