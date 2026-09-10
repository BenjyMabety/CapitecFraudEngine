CREATE DATABASE IF NOT EXISTS CapitecFraudDb;
USE CapitecFraudDb;

-- Processed Files Table
CREATE TABLE IF NOT EXISTS ProcessedFiles (
    FileId INT AUTO_INCREMENT PRIMARY KEY,
    FileName VARCHAR(255) NOT NULL,
    FileHash VARCHAR(64) NOT NULL UNIQUE,
    IngestedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    RecordCount INT NOT NULL
);

-- Transaction Records Table
CREATE TABLE IF NOT EXISTS Transactions (
    TransactionId VARCHAR(64) PRIMARY KEY,
    FileId INT NOT NULL,
    AccountNumber VARCHAR(50) NOT NULL,
    AccountName VARCHAR(100) NOT NULL,
    TransactionDate DATETIME NOT NULL,
    Amount DECIMAL(18, 2) NOT NULL,
    TransactionType INT NOT NULL,
    Merchant VARCHAR(100),
    FOREIGN KEY (FileId) REFERENCES ProcessedFiles(FileId) ON DELETE CASCADE
);

-- Dynamic Fraud Rules Table
CREATE TABLE IF NOT EXISTS FraudRules (
    RuleId INT AUTO_INCREMENT PRIMARY KEY,
    RuleName VARCHAR(100) NOT NULL,
    FieldName VARCHAR(50) NOT NULL,
    Operator VARCHAR(10) NOT NULL,
    ThresholdValue VARCHAR(50) NOT NULL,
    IsActive BOOLEAN DEFAULT TRUE
);

-- Triggered Fraud Alerts Table
CREATE TABLE IF NOT EXISTS FraudAlerts (
    AlertId INT AUTO_INCREMENT PRIMARY KEY,
    TransactionId VARCHAR(64) NOT NULL,
    RuleId INT NOT NULL,
    TriggeredAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (TransactionId) REFERENCES Transactions(TransactionId) ON DELETE CASCADE,
    FOREIGN KEY (RuleId) REFERENCES FraudRules(RuleId) ON DELETE CASCADE
);

-- Seed Initial Default Fraud Rule
INSERT INTO FraudRules (RuleName, FieldName, Operator, ThresholdValue, IsActive)
VALUES ('High Value Withdrawal Check', 'Amount', '<', '-50000', TRUE)
ON DUPLICATE KEY UPDATE RuleName=RuleName;
-- Seed Additional Real-World Amount-Based Fraud Rules
INSERT INTO FraudRules (RuleName, FieldName, Operator, ThresholdValue, IsActive)
VALUES 
  ('Large Outbound Payment Alert', 'Amount', '<', '-100000.00', TRUE),
  ('Card Testing Micro-Transaction Alert', 'Amount', '>', '-10.00', TRUE),
  ('High-Value Inbound Deposit Check', 'Amount', '>', '250000.00', TRUE),
  ('Zero-Value Authorization Attempt', 'Amount', '==', '0.00', TRUE)
ON DUPLICATE KEY UPDATE RuleName=RuleName;

-- Create Users Table
CREATE TABLE IF NOT EXISTS Users (
    UserId INT AUTO_INCREMENT PRIMARY KEY,
    UserName VARCHAR(100) NOT NULL UNIQUE,
    UserPassword VARCHAR(255) NOT NULL,
    UserCreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    UserLastLogin DATETIME NULL
);

-- Seed Initial Admin User (Username: admin, Password: Password123!)
INSERT INTO Users (UserName, UserPassword, UserCreatedDate)
VALUES ('admin', 'a109e36947ad56de1dca1cc49f0ef8ac9ad9a7b1aa0df41fb3c4cb73c1ff01ea', NOW())
ON DUPLICATE KEY UPDATE UserName=UserName;