# Capitec Fraud Engine

A **.NET 8 backend service** for real-time transaction ingestion, rule-based fraud detection, auditing, and file lifecycle management.

---

## Core Architecture & ETL Pipeline

The ETL pipeline is driven by an automated background service (`FileCollectorService`) that monitors the `data/collector/` directory every 2 seconds for incoming `.csv` transaction files.

**Pipeline Flow:**
[CSV Drop] -> [SHA-256 & Structural Check] -> [MySQL Storage] -> [Fraud Engine] -> [Zip Archive]


### Ingestion & SHA-256 Deduplication
- Computes a SHA-256 hash of file content.
- Queries `ProcessedFiles` table for duplicates.
- Duplicate files are flagged and deleted immediately.

### Structural Validation
- Validates header integrity (`TransactionId`, `AccountNumber`, etc.).
- Invalid/missing headers halt processing and purge the file.

### Parsing & Database Persistence
- CSV rows mapped to `TransactionRecord` domain models.
- Metadata written to `ProcessedFiles` via Dapper.
- Records batch inserted into `Transactions`.

### Fraud Evaluation
- Transactions evaluated against active rules in `FraudRules`.
- Violations generate entries in `FraudAlerts` and structured warning logs.

### Archiving
- `ArchiveService` compresses processed CSVs into `.zip` inside `data/archive/`.
- Source CSV deleted after archiving.

---

## Dynamic Fraud Rule Engine

The `FraudEvaluationEngine` applies flexible rules stored in MySQL without requiring code rebuilds or redeployment.

### Rule Evaluation Logic
- **Numeric Evaluator (`NumericRuleEvaluator`)**
  - Evaluates monetary values (`Amount`).
  - Handles directional logic (negative = debit/withdrawal, positive = credit/deposit).

---

## Archiving, Retention & Logging

- **Archive Retention Service (`DeletedArchivesService`)**
  - Periodically cleans archive folder.
  - Deletes `.zip` files older than `ArchiveRetentionPeriodHours` (default: 24h).
- **Rolling File Logging**
  - Logs events and fraud triggers to `data/logs/app_YYYYMMDD.log` using `NReco.Logging.File`.

---

## Database Schema

The backend uses **MySQL (CapitecFraudDb)** initialized via `install_v1.sql`.

| Table          | Purpose                                                                 |
|----------------|-------------------------------------------------------------------------|
| ProcessedFiles | Audit metadata for ingested CSVs (Filename, SHA-256, record count, etc.)|
| Transactions   | Detailed ledger of all ingested transactions linked to `ProcessedFiles` |
| FraudRules     | Dynamic rule definitions (FieldName, Operator, ThresholdValue, IsActive)|
| FraudAlerts    | Logged violations linking transactions to broken rules                  |
| Users          | User credentials with SHA-256 password hashing                          |

---

## Testing Utilities & Test Suite

### Dummy File Generator
- Endpoint: `POST /api/v1/generator/sample-file`
- Generates sample CSV with 10 test records (Deposit, Withdrawal, Transfer, Payment).
- Output: File placed in `data/collector/` to trigger ETL.

### Unit & Integration Tests
- Covers evaluation engine, file parsing, and rule logic.
- Repository: [FraudEngine.Tests](https://github.com/BenjyMabety/FraudEngine.Tests)

---

## Continuous Integration / Continuous Deployment (CI/CD)

The **FraudEngine.Tests** repository is fully integrated with **GitHub Actions** for automated CI/CD.

### Workflow Overview
- **Trigger:** Runs automatically on every commit or pull request to the `main` branch.
- **Pipeline Steps:**
  1. Checkout repository
  2. Setup .NET 8 environment
  3. Restore dependencies
  4. Build solution
  5. Run unit and integration tests (`FraudEngine.Tests`)
  6. Publish test results and artifacts

### GitHub Actions Workflow File (`.github/workflows/ci.yml`)
```yaml
name: Build and Run FraudEngine Tests

on:
  push:
    branches: [ "main", "master" ]
  pull_request:
    branches: [ "main", "master" ]

jobs:
  build-and-test:
    runs-on: ubuntu-latest

    steps:
    - name: Checkout Main Repository
      uses: actions/checkout@v4
      with:
        path: 'CapitecFraudEngine'

    - name: Checkout Test Repository
      uses: actions/checkout@v4
      with:
        repository: 'BenjyMabety/FraudEngine.Tests'
        ref: 'master'
        path: 'FraudEngine.Tests'

    - name: Setup .NET SDK
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '8.0.x'

    - name: Restore Main Engine
      run: dotnet restore CapitecFraudEngine/CapitecFraudEngine.csproj

    - name: Build Main Engine
      run: dotnet build CapitecFraudEngine/CapitecFraudEngine.csproj --no-restore --configuration Release

    - name: Restore and Run Tests
      run: |
        TEST_PROJ=$(find FraudEngine.Tests -name "*.csproj" | head -n 1)
        echo "Found test project at: $TEST_PROJ"
        dotnet test "$TEST_PROJ" --configuration Release --verbosity normal
```


### Front End Testing
- Repository: [fraud.portal](https://github.com/BenjyMabety/fraud-portal)

---

## API Reference

### Authentication
| Method | Endpoint              | Description                                           |
|--------|-----------------------|-------------------------------------------------------|
| POST   | `/api/v1/auth/login`  | Authenticates users against SHA-256 hashed credentials|

### Fraud Rules
| Method | Endpoint              | Description                                           |
|--------|-----------------------|-------------------------------------------------------|
| GET    | `/api/v1/rules`       | Retrieves all configured fraud rules                  |
| POST   | `/api/v1/rules`       | Creates a new dynamic fraud rule                      |
| PUT    | `/api/v1/rules/{id}`  | Updates an existing fraud rule by ID                  |
| DELETE | `/api/v1/rules/{id}`  | Deletes a fraud rule by ID                            |

### Alerts & Auditing
| Method | Endpoint              | Description                                           |
|--------|-----------------------|-------------------------------------------------------|
| GET    | `/api/v1/alerts`      | Fetches all triggered fraud alerts with details       |
| GET    | `/api/v1/files`       | Retrieves audit logs of all processed files           |

### Testing Utilities
| Method | Endpoint                      | Description                                         |
|--------|-------------------------------|-----------------------------------------------------|
| POST   | `/api/v1/generator/sample-file` | Generates sample CSV in collector directory         |

---

## Deployment with Docker

Pull and run the complete system (Database, Engine API, and UI Portal) via DockerHub.

### 1. Running on Docker
1. Create Docker Network
```bash
docker network create capitec-net
2. Start MySQL Database Container
docker run --name fraud-db-container --network capitec-net -p 0:3306 -d benmbete08/capitec-fraud-db
3. Start Backend Fraud Engine Container
docker run -d --name capitec-fraud-app --network capitec-net -p 8080:8080 benmbete08/capitec-fraud-engine
4. Start Angular UI Portal Container
docker run -d --name fraud-portal-app --network capitec-net -p 4200:4200 benmbete08/fraud-portal-ui


Accessing the Application
Frontend Portal: http://localhost:4200



Seed Data:
Username:admin
Password:Password123!

