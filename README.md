---
title: Infoaidtech Backend
emoji: 💊
colorFrom: blue
colorTo: indigo
sdk: docker
app_port: 7860
pinned: false
license: mit
short_description: Aidly PMS .NET 10 Web API Backend
---

# Aidly PMS - Backend Web API

Enterprise Pharmacy & Retail Management System (PMS) backend built with **.NET 10 Web API**, **Dapper**, and **PostgreSQL (Supabase)**.

## Overview

Aidly PMS is a high-performance, modular ERP/PMS solution designed for retail and pharmacy businesses. It delivers fast inventory tracking, real-time point of sale (POS) transactions, multi-account ledger bookkeeping, customer credit management, supplier purchases, and requisition planning.

## Tech Stack

- **Runtime**: .NET 10.0 (ASP.NET Core Web API)
- **Database**: PostgreSQL (Supabase) via Npgsql
- **Data Access**: Dapper with raw optimized SQL
- **Container**: Multi-stage Linux Docker image (non-root `USER 1000`)
- **Port**: `7860` (Hugging Face Spaces standard)

## Key Modules & Endpoints

- **Health Check**: `GET /health` — Returns `{"status":"Healthy"}`
- **Swagger / OpenAPI**: `GET /swagger`
- **Dashboard**: `GET /api/dashboard/stats`, `GET /api/dashboard/low-stock-products`
- **Point of Sale (POS)**: `POST /api/pos/orders`, `GET /api/pos/receipt/{id}`
- **Sales & Returns**: `GET /api/sales`, `POST /api/sales/returns`
- **Inventory & Products**: `GET /api/products`, `POST /api/products`, `POST /api/requisitions`
- **Accounting & Ledger**: `GET /api/accounts`, `POST /api/accounts/entries`, `GET /api/expenses`
- **Customer & Due Collection**: `GET /api/customers`, `POST /api/due/collect`
- **Suppliers & Purchases**: `GET /api/suppliers`, `POST /api/purchases`

## Hugging Face Deployment Notes

- Configured to run on Hugging Face Spaces using the **Docker** runtime.
- Runs on non-privileged port `7860` as required by Hugging Face Spaces.
- Memory tuned for container cgroups via `DOTNET_GCHeapHardLimitPercent=75`.
