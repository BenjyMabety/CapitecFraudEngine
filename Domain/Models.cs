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

public static class TransactionTypeConstants
{
    public const int Deposit = 1;
    public const int Withdrawal = 2;
    public const int Transfer = 3;
    public const int Payment = 4;
    public const int Unknown = 0;

    public static int FromString(string typeStr) => typeStr?.Trim().ToLowerInvariant() switch
    {
        "deposit" => Deposit,
        "withdrawal" => Withdrawal,
        "transfer" => Transfer,
        "payment" => Payment,
        _ => Unknown
    };

    public static string ToString(int typeId) => typeId switch
    {
        Deposit => "Deposit",
        Withdrawal => "Withdrawal",
        Transfer => "Transfer",
        Payment => "Payment",
        _ => "Unknown"
    };
}

public class TransactionRecord
{
    public string TransactionId { get; set; } = string.Empty;
    public int FileId { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public int TransactionType { get; set; }
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