namespace CapitecFraudEngine.Domain;

public enum TransactionType
{
    Deposit,
    Withdrawal,
    Transfer,
    Payment
}

public class ProcessedFile
{
    public int FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public DateTime IngestedAt { get; set; }
    public int RecordCount { get; set; }
}

public class TransactionRecord
{
    public string TransactionId { get; set; } = string.Empty;
    public int FileId { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string Merchant { get; set; } = string.Empty;
}

public class FraudRule
{
    public int RuleId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string ThresholdValue { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class FraudAlertDto
{
    public int AlertId { get; set; }
    public DateTime TriggeredAt { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string RuleBroken { get; set; } = string.Empty;
    public string RuleCondition { get; set; } = string.Empty;
}

public class LoginRequestDto
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class UserDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime UserCreatedDate { get; set; }
    public DateTime? UserLastLogin { get; set; }
}

public class LoginResponseDto
{
    public string Message { get; set; } = string.Empty;
    public UserDto User { get; set; } = new();
}