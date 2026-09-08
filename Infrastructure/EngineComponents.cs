using CapitecFraudEngine.Domain;
using System.Globalization;

namespace CapitecFraudEngine.Infrastructure;

public interface IFraudRuleEvaluator
{
    bool CanEvaluate(string fieldName);
    bool IsBroken(TransactionRecord record, FraudRule rule);
}

public class NumericRuleEvaluator : IFraudRuleEvaluator
{
    public bool CanEvaluate(string fieldName) =>
        fieldName.Equals("Amount", StringComparison.OrdinalIgnoreCase);

    public bool IsBroken(TransactionRecord record, FraudRule rule)
    {
        // Use InvariantCulture to parse the threshold string safely
        if (!decimal.TryParse(rule.ThresholdValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var threshold))
            return false;

        return rule.Operator switch
        {
            ">" => record.Amount > threshold,
            "<" => record.Amount < threshold,
            "==" => record.Amount == threshold,
            ">=" => record.Amount >= threshold,
            "<=" => record.Amount <= threshold,
            _ => false
        };
    }
}

public class StringRuleEvaluator : IFraudRuleEvaluator
{
    public bool CanEvaluate(string fieldName) =>
        fieldName.Equals("Type", StringComparison.OrdinalIgnoreCase) ||
        fieldName.Equals("TransactionType", StringComparison.OrdinalIgnoreCase) ||
        fieldName.Equals("Merchant", StringComparison.OrdinalIgnoreCase);

    public bool IsBroken(TransactionRecord record, FraudRule rule)
    {
        var actualValue = rule.FieldName.ToLower() switch
        {
            "type" or "transactiontype" => record.TransactionType,
            "merchant" => record.Merchant,
            _ => string.Empty
        };

        return rule.Operator switch
        {
            "==" => actualValue.Equals(rule.ThresholdValue, StringComparison.OrdinalIgnoreCase),
            "!=" => !actualValue.Equals(rule.ThresholdValue, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}

public class FraudEvaluationEngine
{
    private readonly IEnumerable<IFraudRuleEvaluator> _evaluators;

    public FraudEvaluationEngine(IEnumerable<IFraudRuleEvaluator> evaluators)
    {
        _evaluators = evaluators;
    }

    public List<int> EvaluateRecord(TransactionRecord record, IEnumerable<FraudRule> rules)
    {
        var brokenRuleIds = new List<int>();

        foreach (var rule in rules.Where(r => r.IsActive))
        {
            var evaluator = _evaluators.FirstOrDefault(e => e.CanEvaluate(rule.FieldName));
            if (evaluator != null && evaluator.IsBroken(record, rule))
            {
                brokenRuleIds.Add(rule.RuleId);
            }
        }

        return brokenRuleIds;
    }
}