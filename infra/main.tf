###############################################################################
# main.tf
#
# NutriForge modular-monolith on Azure Container Apps (ADR-0010, ADR-0013):
#
#   Resource Group
#     ├─ User-Assigned Managed Identity  (the app's workload identity)
#     ├─ Log Analytics Workspace + Application Insights (workspace-based)
#     ├─ Key Vault (RBAC)  ──grant──▶ UAMI: "Key Vault Secrets User"
#     │     └─ secret: redis-connection (Redis key kept OUT of app config)
#     ├─ Container Registry (ACR)  ──grant──▶ UAMI: "AcrPull"
#     ├─ PostgreSQL Flexible Server (Entra-only)  ──▶ appdb + auditdb (ADR-0011)
#     ├─ Azure Cache for Redis
#     ├─ Container Apps Environment (wired to Log Analytics)
#     ├─ Container App: API  (external ingress; identity ▶ UAMI)
#     └─ Container App Job: import  (nightly cron; identity ▶ UAMI)
#
# No secret is ever written to container env in plaintext. The Redis key lives
# in Key Vault and is surfaced as a Key Vault-referenced ACA secret resolved by
# the UAMI. Postgres uses Entra-only auth -> no password anywhere.
###############################################################################

data "azurerm_client_config" "current" {}

locals {
  # Deterministic, collision-resistant suffix so globally-unique names hold.
  name_suffix = substr(sha1("${var.app_name}-${var.environment}-${data.azurerm_client_config.current.subscription_id}"), 0, 6)
  base        = "${var.app_name}-${var.environment}"
  # ACR names are alphanumeric only (no hyphens), 5-50 chars.
  acr_name = lower(replace("acr${var.app_name}${var.environment}${local.name_suffix}", "-", ""))

  tags = merge(
    {
      environment = var.environment
      app         = var.app_name
      managed-by  = "terraform"
    },
    var.tags
  )
}

# --- Resource group --------------------------------------------------------- #
resource "azurerm_resource_group" "main" {
  name     = "rg-${local.base}"
  location = var.location
  tags     = local.tags
}

# --- Workload identity (User-Assigned Managed Identity) --------------------- #
resource "azurerm_user_assigned_identity" "app" {
  name                = "id-${local.base}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  tags                = local.tags
}

# --- Observability ---------------------------------------------------------- #
resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${local.base}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "PerGB2018"
  retention_in_days   = var.log_retention_days
  tags                = local.tags
}

resource "azurerm_application_insights" "main" {
  name                = "appi-${local.base}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  workspace_id        = azurerm_log_analytics_workspace.main.id
  application_type    = "web"
  tags                = local.tags
}

# --- Key Vault (RBAC-authorized) -------------------------------------------- #
resource "azurerm_key_vault" "main" {
  name                = "kv-${var.app_name}-${var.environment}-${local.name_suffix}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  # Azure RBAC for data-plane authorization (not legacy access policies).
  rbac_authorization_enabled = true

  purge_protection_enabled = var.environment == "prod"
  tags                     = local.tags
}

# The app's identity may READ secrets from the vault.
resource "azurerm_role_assignment" "app_kv_secrets_user" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}

# The principal running Terraform must be able to WRITE secrets during apply.
resource "azurerm_role_assignment" "deployer_kv_secrets_officer" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

# --- Container registry ----------------------------------------------------- #
resource "azurerm_container_registry" "main" {
  name                = local.acr_name
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = var.acr_sku
  admin_enabled       = false # identity-based pulls only -- no registry username/password
  tags                = local.tags
}

# The app's identity may PULL images.
resource "azurerm_role_assignment" "app_acr_pull" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}

# The CI/CD principal (same federated identity that runs Terraform) may PUSH images.
resource "azurerm_role_assignment" "deployer_acr_push" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPush"
  principal_id         = data.azurerm_client_config.current.object_id
}

# --- Data tier: PostgreSQL Flexible Server (Entra-only) --------------------- #
resource "azurerm_postgresql_flexible_server" "main" {
  count                         = var.enable_postgres ? 1 : 0
  name                          = "psql-${var.app_name}-${var.environment}-${local.name_suffix}"
  resource_group_name           = azurerm_resource_group.main.name
  location                      = azurerm_resource_group.main.location
  version                       = var.postgres_version
  sku_name                      = var.postgres_sku_name
  storage_mb                    = var.postgres_storage_mb
  zone                          = var.zone_redundant ? "1" : null
  public_network_access_enabled = true # tighten to Private Endpoint for prod

  # Entra-only auth: no admin password exists. The app authenticates with its
  # Managed Identity (Npgsql obtains an Entra access token via the UAMI).
  authentication {
    active_directory_auth_enabled = true
    password_auth_enabled         = false
    tenant_id                     = data.azurerm_client_config.current.tenant_id
  }

  tags = local.tags
}

resource "azurerm_postgresql_flexible_server_active_directory_administrator" "main" {
  count               = var.enable_postgres ? 1 : 0
  server_name         = azurerm_postgresql_flexible_server.main[0].name
  resource_group_name = azurerm_resource_group.main.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  object_id           = var.postgres_admin_object_id
  principal_name      = var.postgres_admin_login
  principal_type      = "Group" # an Entra group is recommended for the admin
}

# Operational database (foods, recipes, diary, plans, outbox, idempotency).
resource "azurerm_postgresql_flexible_server_database" "app" {
  count     = var.enable_postgres ? 1 : 0
  name      = "appdb"
  server_id = azurerm_postgresql_flexible_server.main[0].id
  collation = "en_US.utf8"
  charset   = "UTF8"
}

# Append-only audit database, OUTSIDE the operational DB (ADR-0011).
resource "azurerm_postgresql_flexible_server_database" "audit" {
  count     = var.enable_postgres ? 1 : 0
  name      = "auditdb"
  server_id = azurerm_postgresql_flexible_server.main[0].id
  collation = "en_US.utf8"
  charset   = "UTF8"
}

# pgvector is deferred to Phase 5 (ROADMAP). When enabled, allowlist it here and
# run `CREATE EXTENSION vector;` against appdb:
# resource "azurerm_postgresql_flexible_server_configuration" "extensions" {
#   count     = var.enable_postgres ? 1 : 0
#   name      = "azure.extensions"
#   server_id = azurerm_postgresql_flexible_server.main[0].id
#   value     = "VECTOR"
# }

# --- Cache: Azure Cache for Redis ------------------------------------------- #
resource "azurerm_redis_cache" "main" {
  name                          = "redis-${var.app_name}-${var.environment}-${local.name_suffix}"
  resource_group_name           = azurerm_resource_group.main.name
  location                      = azurerm_resource_group.main.location
  capacity                      = var.redis_capacity
  family                        = var.redis_sku_name == "Premium" ? "P" : "C"
  sku_name                      = var.redis_sku_name
  minimum_tls_version           = "1.2"
  public_network_access_enabled = true # tighten to Private Endpoint for prod
  tags                          = local.tags
}

# Redis connection string lives in Key Vault, never in container env in plaintext.
resource "azurerm_key_vault_secret" "redis" {
  name         = "redis-connection"
  key_vault_id = azurerm_key_vault.main.id
  value        = "${azurerm_redis_cache.main.hostname}:${azurerm_redis_cache.main.ssl_port},password=${azurerm_redis_cache.main.primary_access_key},ssl=True,abortConnect=False"
  tags         = local.tags

  # The deployer needs the Secrets Officer grant before it can write.
  depends_on = [azurerm_role_assignment.deployer_kv_secrets_officer]
}

# --- Container Apps environment --------------------------------------------- #
resource "azurerm_container_app_environment" "main" {
  name                       = "cae-${local.base}"
  resource_group_name        = azurerm_resource_group.main.name
  location                   = azurerm_resource_group.main.location
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
  tags                       = local.tags
}

# Env shared by the API app and the import job. Postgres uses Entra-only auth:
# the UAMI client id is the username and Npgsql supplies an Entra token as the
# password at runtime. Post-provision, map the UAMI to a Postgres role:
#   CREATE ROLE "<uami-name>" WITH LOGIN;  -- via the Entra admin
#   GRANT ALL ON DATABASE appdb, auditdb TO "<uami-name>";
locals {
  pg_host = var.enable_postgres ? azurerm_postgresql_flexible_server.main[0].fqdn : ""
  common_env = concat(
    [
      { name = "ASPNETCORE_ENVIRONMENT", value = var.environment == "prod" ? "Production" : "Development" },
      { name = "APPLICATIONINSIGHTS_CONNECTION_STRING", value = azurerm_application_insights.main.connection_string },
      { name = "AZURE_CLIENT_ID", value = azurerm_user_assigned_identity.app.client_id },
      { name = "KeyVault__Uri", value = azurerm_key_vault.main.vault_uri },
    ],
    var.enable_postgres ? [
      # The env-var NAMES must match the connection names the app resolves: appdb/auditdb (the Aspire
      # resource names from AppHost.cs, read via GetConnectionString in DependencyInjection.cs). Aspire
      # surfaces them locally as ConnectionStrings__appdb/__auditdb; ACA must inject the SAME keys.
      { name = "ConnectionStrings__appdb", value = "Host=${local.pg_host};Database=appdb;Username=${azurerm_user_assigned_identity.app.client_id};SSL Mode=Require;Trust Server Certificate=true" },
      { name = "ConnectionStrings__auditdb", value = "Host=${local.pg_host};Database=auditdb;Username=${azurerm_user_assigned_identity.app.client_id};SSL Mode=Require;Trust Server Certificate=true" },
    ] : []
  )
}

# --- Container App: API (the deployable; external ingress) ------------------ #
resource "azurerm_container_app" "api" {
  name                         = "ca-${local.base}-api"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"
  tags                         = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.app.id]
  }

  registry {
    server   = azurerm_container_registry.main.login_server
    identity = azurerm_user_assigned_identity.app.id
  }

  # Redis connection from Key Vault, resolved by the UAMI.
  secret {
    name                = "redis-connection"
    key_vault_secret_id = azurerm_key_vault_secret.redis.versionless_id
    identity            = azurerm_user_assigned_identity.app.id
  }

  ingress {
    external_enabled = true
    target_port      = var.api_target_port
    transport        = "auto"
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = var.api_min_replicas
    max_replicas = var.api_max_replicas

    container {
      name   = "api"
      image  = var.api_image
      cpu    = var.container_cpu
      memory = var.container_memory

      dynamic "env" {
        for_each = local.common_env
        content {
          name  = env.value.name
          value = env.value.value
        }
      }
      # The app's Redis client is registered as AddRedisClient("cache"), which reads
      # ConnectionStrings__cache. The VALUE is the Key Vault-referenced ACA secret (resolved by the
      # UAMI); only the env-var NAME has to match the "cache" Aspire resource name.
      env {
        name        = "ConnectionStrings__cache"
        secret_name = "redis-connection"
      }
    }
  }

  # CD swaps the image per release (az containerapp update); ignore drift on it.
  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}

# --- Container App Job: nightly import (USDA / Open Food Facts) -------------- #
# A Job (not an always-on app) scales to zero between runs -- the importer is
# scheduled, never on a request path (system-design §5).
resource "azurerm_container_app_job" "import" {
  name                         = "caj-${local.base}-import"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  location                     = azurerm_resource_group.main.location
  replica_timeout_in_seconds   = 1800
  replica_retry_limit          = 1
  tags                         = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.app.id]
  }

  registry {
    server   = azurerm_container_registry.main.login_server
    identity = azurerm_user_assigned_identity.app.id
  }

  secret {
    name                = "redis-connection"
    key_vault_secret_id = azurerm_key_vault_secret.redis.versionless_id
    identity            = azurerm_user_assigned_identity.app.id
  }

  schedule_trigger_config {
    cron_expression          = "0 3 * * *" # 03:00 UTC nightly
    parallelism              = 1
    replica_completion_count = 1
  }

  template {
    container {
      name   = "import"
      image  = var.worker_image
      cpu    = var.container_cpu
      memory = var.container_memory

      dynamic "env" {
        for_each = local.common_env
        content {
          name  = env.value.name
          value = env.value.value
        }
      }
      # The app's Redis client is registered as AddRedisClient("cache"), which reads
      # ConnectionStrings__cache. The VALUE is the Key Vault-referenced ACA secret (resolved by the
      # UAMI); only the env-var NAME has to match the "cache" Aspire resource name.
      env {
        name        = "ConnectionStrings__cache"
        secret_name = "redis-connection"
      }
    }
  }

  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}
