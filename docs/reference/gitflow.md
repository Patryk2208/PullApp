# GitFlow Best Practices for Monorepo Microservices

## Overview

This document defines the Git branching strategy and workflow for our monorepo containing multiple microservices. We use an **adapted GitFlow model** with service-aware branch naming and path-based CI/CD.

## Branch Structure

### Permanent Branches

| Branch | Purpose | Protection |
|--------|---------|------------|
| `main` | Production-ready code. Every commit represents a deployable state of ALL services. | Require PR, passing CI, signed commits |
| `develop` | Integration branch for features. Source of truth for next releases. | Require PR, passing CI |

### Temporary Branches

All temporary branches follow this naming pattern: type/service/description


#### Branch Types

| Type | Purpose | Created From | Merged Into |
|------|---------|--------------|--------------|
| `feature` | New functionality or improvements | `develop` | `develop` |
| `release` | Prepare a service for production | `develop` | `main` + `develop` |
| `hotfix` | Urgent production fix | `main` | `main` + `develop` |

#### Naming Examples

```bash
# Features
feature/trip-planner/initial-structure
feature/account-service/grpc-interface
feature/gateway/rate-limiting
feature/notifications/email-templates

# Releases
release/account-service/v1.2.0
release/payment-service/v2.1.0
release/gateway/v1.0.0

# Hotfixes
hotfix/auth/token-validation-bug
hotfix/payments/currency-conversion
hotfix/gateway/ssl-certificate