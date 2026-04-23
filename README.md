# HNG Stage 2 – Intelligence Query Engine

## Overview

This project is a backend API that transforms stored demographic profile data into a Queryable Intelligence Engine.

It allows clients to:
- Filter profiles using multiple conditions
- Combine filters efficiently
- Sort and paginate results
- Query using natural language

---

## Base URL

https://hng-stage0-backend-production-68c6.up.railway.app

---

## Endpoints

### Get Profiles
GET /api/profiles

### Natural Language Search
GET /api/profiles/search?q=<query>

Example:
GET /api/profiles/search?q=young males from nigeria

---

## Filtering Parameters

- gender (male / female)
- age_group (child, teenager, adult, senior)
- country_id (NG, KE, etc.)
- min_age
- max_age
- min_gender_probability
- min_country_probability

Example:
GET /api/profiles?gender=male&country_id=NG&min_age=25

---

## Sorting

- sort_by: age | created_at | gender_probability
- order: asc | desc

Example:
GET /api/profiles?sort_by=age&order=desc

---

## Pagination

- page (default: 1)
- limit (default: 10, max: 50)

Example:
GET /api/profiles?page=1&limit=10

Response format:

{
  "status": "success",
  "page": 1,
  "limit": 10,
  "total": 2026,
  "data": []
}

---

## Natural Language Query Mapping

Examples:

- "young males" → gender=male + min_age=16 + max_age=24
- "females above 30" → gender=female + min_age=30
- "people from angola" → country_id=AO
- "adult males from kenya" → gender=male + age_group=adult + country_id=KE
- "teenagers above 17" → age_group=teenager + min_age=17

Rules:
- Rule-based parsing only (no AI)
- "young" = age 16–24

If query cannot be interpreted:

{
  "status": "error",
  "message": "Unable to interpret query"
}

---

## Error Handling

All errors follow this format:

{
  "status": "error",
  "message": "<error message>"
}

---

## Database Structure

Fields:

- id (UUID v7)
- name (unique)
- gender
- gender_probability
- age
- age_group
- country_id
- country_name
- country_probability
- created_at

---

## Data Seeding

- Database seeded with 2026 profiles
- Loaded from JSON file
- Duplicate records are prevented

---

## Tech Stack

- .NET 8
- Entity Framework Core
- SQLite
- Railway

---

## CORS

Access-Control-Allow-Origin: *

---

## Notes

- All timestamps are UTC (ISO 8601)
- All IDs are UUID v7
- Response structure follows required format exactly
