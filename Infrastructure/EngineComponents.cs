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
        if (rule.FieldName.Equals("Merchant", StringComparison.OrdinalIgnoreCase))
        {
            return rule.Operator switch
            {
                "==" => record.Merchant.Equals(rule.ThresholdValue, StringComparison.OrdinalIgnoreCase),
                "!=" => !record.Merchant.Equals(rule.ThresholdValue, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        // Handles TransactionType evaluation (supports comparison by integer ID or string name)
        var actualTypeId = record.TransactionType;
        var thresholdTypeId = int.TryParse(rule.ThresholdValue, out var parsedId)
            ? parsedId
            : TransactionTypeConstants.FromString(rule.ThresholdValue);

        return rule.Operator switch
        {
            "==" => actualTypeId == thresholdTypeId,
            "!=" => actualTypeId != thresholdTypeId,
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