###############################################################################
# outputs.tf
#
# Surfaced for the CD pipeline (the app deploy job reads the ACR + app names)
# and for operators. No secret values are output.
###############################################################################

output "resource_group_name" {
  description = "Resource group containing all environment resources."
  value       = azurerm_resource_group.main.name
}

output "acr_login_server" {
  description = "ACR login server -- consumed by cd-app.yml to tag/push images."
  value       = azurerm_container_registry.main.login_server
}

output "acr_name" {
  description = "ACR name -- consumed by cd-app.yml for `az acr login`."
  value       = azurerm_container_registry.main.name
}

output "api_app_name" {
  description = "API Container App name -- consumed by cd-app.yml for `az containerapp update`."
  value       = azurerm_container_app.api.name
}

output "import_job_name" {
  description = "Import Container App Job name -- consumed by cd-app.yml to update the image."
  value       = azurerm_container_app_job.import.name
}

output "api_fqdn" {
  description = "Public FQDN of the API ingress."
  value       = azurerm_container_app.api.latest_revision_fqdn
}

output "app_identity_client_id" {
  description = "Client ID of the app's User-Assigned Managed Identity."
  value       = azurerm_user_assigned_identity.app.client_id
}

output "app_identity_principal_id" {
  description = "Principal (object) ID of the app's User-Assigned Managed Identity."
  value       = azurerm_user_assigned_identity.app.principal_id
}

output "app_identity_name" {
  description = "UAMI name -- use it for the post-provision Postgres role grant (CREATE ROLE \"<name>\")."
  value       = azurerm_user_assigned_identity.app.name
}

output "key_vault_uri" {
  description = "Key Vault data-plane URI."
  value       = azurerm_key_vault.main.vault_uri
}

output "application_insights_connection_string" {
  description = "App Insights connection string (treat as sensitive)."
  value       = azurerm_application_insights.main.connection_string
  sensitive   = true
}

output "postgres_fqdn" {
  description = "PostgreSQL Flexible Server FQDN (null when Postgres is disabled)."
  value       = var.enable_postgres ? azurerm_postgresql_flexible_server.main[0].fqdn : null
}

output "redis_hostname" {
  description = "Azure Cache for Redis hostname."
  value       = azurerm_redis_cache.main.hostname
}
