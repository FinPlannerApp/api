# 🖥️ Financial Planner — Backend

> ASP.NET Core (.NET 10) REST API for Financial Planner — built with Clean Architecture, Entity Framework Core, Redis, and ASP.NET Core Identity.

## Tech Stack

| Technology | Purpose |
|------------|---------|
| **.NET 10 / ASP.NET Core** | Web API framework |
| **Entity Framework Core** | ORM — PostgreSQL migrations, LINQ queries |
| **ASP.NET Core Identity** | User management, password hashing (Argon2id), roles |
| **Redis (StackExchange.Redis)** | OTP store, pending registration cache (with TTL) |
| **JWT Bearer** | Short-lived access token authentication |
| **Refresh Tokens** | Long-lived token rotation in PostgreSQL |
| **Clean Architecture** | Domain / Application / Infrastructure / Presentation separation |
| **SMTP / IEmailService** | Styled HTML transactional emails (OTP, password reset) |

## Project Structure

```
src/
├── Domain/           # Pure C# entities, no framework dependencies
├── Application/      # Business logic: services, DTOs, contracts, validators
├── Infrastructure/   # DbContext, EF migrations, email service, Redis
└── Presentation/     # ASP.NET Controllers — thin HTTP layer only
```

## Running Locally

```bash
# Configure appsettings.json with PostgreSQL connstring, Redis, JWT, SMTP
dotnet run --project src/Presentation/API.csproj
```

API will be available at `http://localhost:5000`.

## Auth Flow Summary

1. **Register:** `POST /api/auth/register` → stores in Redis + sends OTP email
2. **Verify OTP:** `POST /api/auth/verify-registration` → creates user in DB
3. **Login:** `POST /api/auth/login` → returns JWT + Refresh Token
4. **Refresh:** `POST /api/auth/refresh-token` → rotates refresh token
5. **Logout:** `POST /api/auth/logout` → revokes token, clears session
6. **Forgot PW:** `POST /api/auth/forgot-password` → sends OTP email
7. **Reset PW:** `POST /api/auth/reset-password` → validates OTP + updates pw

## Key Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /api/dashboard/summary` | Total income, expenses, net balance |
| `GET /api/dashboard/spending-by-category` | Spending grouped by category |
| `GET /api/dashboard/account-summary` | Accounts grouped by category with balances |
| `GET /api/dashboard/insights` | Deep insights (top/bottom amounts, timelines, extremes) |
| `GET /api/accounts` | All accounts for the authenticated user |
| `GET /api/accounts/{id}/transactions` | Transactions for a specific account |
| `POST /api/transactions/bulk` | Batch transaction create |
| `GET /health` | Health check endpoint |
