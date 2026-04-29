# Insighta Labs+ Backend

Profile Intelligence System with secure authentication, role-based access control, and multi-interface support.

## System Architecture

```
┌──────────────┐     ┌──────────────────┐     ┌──────────────┐
│   CLI Tool   │────▶│                  │◀────│  Web Portal  │
│  (Node.js)   │     │  Backend API     │     │  (Next.js)   │
│              │     │  (ASP.NET Core)  │     │              │
└──────────────┘     │                  │     └──────────────┘
                     │  ┌────────────┐  │
                     │  │  SQLite    │  │
                     │  │  Database  │  │
                     │  └────────────┘  │
                     │                  │
                     │  ┌────────────┐  │
                     │  │  GitHub    │  │
                     │  │  OAuth     │  │
                     │  └────────────┘  │
                     └──────────────────┘
```

The backend is the single source of truth. Both CLI and Web Portal authenticate through GitHub OAuth and interact with the same API endpoints.

## Authentication Flow

### GitHub OAuth with PKCE

**CLI Flow:**
1. CLI generates `state`, `code_verifier`, and `code_challenge` (SHA-256)
2. CLI starts a temporary local HTTP callback server
3. CLI opens the browser to `/auth/github` with PKCE params
4. User authenticates on GitHub
5. GitHub redirects to backend callback
6. Backend exchanges code with GitHub, creates/updates user
7. Backend redirects to CLI's local callback with tokens
8. CLI stores tokens at `~/.insighta/credentials.json`

**Web Flow:**
1. User clicks "Continue with GitHub"
2. Browser redirects to `/auth/github?source=web`
3. After GitHub auth, backend sets HTTP-only cookies
4. Browser redirects to web portal dashboard

### Token Handling

| Token | Type | Expiry | Storage (CLI) | Storage (Web) |
|-------|------|--------|---------------|---------------|
| Access | JWT | 3 min | credentials.json | HTTP-only cookie |
| Refresh | Opaque | 5 min | credentials.json | HTTP-only cookie |

- Refresh tokens are single-use (rotation on every refresh)
- Old refresh tokens are immediately invalidated
- CSRF protection via non-HTTP-only csrf_token cookie for web

## Role Enforcement Logic

Two roles enforced via `[RequireRole]` attribute:

| Role | Permissions |
|------|-------------|
| **admin** | Full access: create profiles, delete profiles, read, search, export |
| **analyst** | Read-only: list profiles, view details, search, export |

- Default role for new users: `analyst`
- Role checked on every `/api/*` request via authorization filter
- Deactivated users (`is_active = false`) receive 403 on all requests

## API Endpoints

### Auth
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/auth/github` | Redirect to GitHub OAuth |
| GET | `/auth/github/callback` | Handle OAuth callback |
| POST | `/auth/token/exchange` | Exchange code + code_verifier for tokens (CLI) |
| POST | `/auth/refresh` | Refresh access token |
| POST | `/auth/logout` | Invalidate tokens |
| GET | `/auth/me` | Get current user |

### Profiles (require `X-API-Version: 1` header)
| Method | Endpoint | Role | Description |
|--------|----------|------|-------------|
| GET | `/api/profiles` | admin, analyst | List with filters, sorting, pagination |
| GET | `/api/profiles/{id}` | admin, analyst | Get profile by ID |
| GET | `/api/profiles/search?q=` | admin, analyst | Natural language search |
| GET | `/api/profiles/export?format=csv` | admin, analyst | Export as CSV |
| POST | `/api/profiles` | admin | Create profile from external APIs |
| DELETE | `/api/profiles/{id}` | admin | Delete profile |

### Pagination Response Format
```json
{
  "status": "success",
  "page": 1,
  "limit": 10,
  "total": 2026,
  "total_pages": 203,
  "links": {
    "self": "/api/profiles?page=1&limit=10",
    "next": "/api/profiles?page=2&limit=10",
    "prev": null
  },
  "data": [...]
}
```

## Natural Language Parsing

The `/api/profiles/search?q=` endpoint interprets plain English queries using rule-based parsing:

| Query | Interpretation |
|-------|---------------|
| "young males" | gender=male, min_age=16, max_age=24 |
| "females above 30" | gender=female, min_age=30 |
| "people from angola" | country_id=AO |
| "adult males from kenya" | gender=male, age_group=adult, country_id=KE |
| "male and female teenagers above 17" | age_group=teenager, min_age=17 |

Supported keywords: young, child, teenager, adult, senior, male, female, above/over N, below/under N, and 60+ country names.

## Rate Limiting

| Scope | Limit |
|-------|-------|
| Auth endpoints (`/auth/*`) | 10 req/min per IP |
| API endpoints | 60 req/min per user |

Returns `429 Too Many Requests` when exceeded.

## Setup

### Prerequisites
- .NET 8 SDK
- GitHub OAuth App (create at github.com/settings/developers)

### Environment Variables
```
Jwt__Secret=your-secret-key-min-32-chars
GitHub__ClientId=your-github-client-id
GitHub__ClientSecret=your-github-client-secret
App__BackendUrl=https://your-backend.railway.app
App__WebPortalUrl=https://your-portal.vercel.app
```

### Run Locally
```bash
dotnet restore
dotnet run
```

### Docker
```bash
docker build -t insighta-backend .
docker run -p 8080:8080 -e PORT=8080 insighta-backend
```

## Database

SQLite with auto-seeded 2026 profiles. Tables:
- `Profiles` — demographic profile data
- `Users` — authenticated users (GitHub OAuth)
- `RefreshTokens` — token rotation tracking

## Request Logging

Every request is logged with: Method, Endpoint, Status Code, Response Time (ms).
