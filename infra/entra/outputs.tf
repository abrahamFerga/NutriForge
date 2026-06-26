###############################################################################
# infra/entra/outputs.tf
#
# These outputs are the bridge to the rest of the system. After `apply`:
#   - feed `auth_authority` + `auth_audience` into the subscription infra's
#     environments/<env>.tfvars (they inject Authentication__* into the API);
#   - feed `vite_auth_*` into the SPA build env (VITE_AUTH_* — see
#     src/NutriForge.Web/.env.example).
###############################################################################

output "api_client_id" {
  description = "API app registration client ID. Also the token audience the API validates."
  value       = azuread_application.api.client_id
}

output "spa_client_id" {
  description = "SPA app registration client ID (MSAL clientId)."
  value       = azuread_application.spa.client_id
}

output "api_identifier_uri" {
  description = "API App ID URI (api://<client-id>)."
  value       = azuread_application_identifier_uri.api.identifier_uri
}

output "api_scope" {
  description = "Full delegated scope the SPA requests (VITE_AUTH_SCOPE)."
  value       = "api://${azuread_application.api.client_id}/access_as_user"
}

output "authority" {
  description = "OIDC authority URL for both the SPA (VITE_AUTH_AUTHORITY) and the API (Authentication:Authority)."
  value       = "https://${var.tenant_subdomain}.ciamlogin.com/${var.tenant_id}/"
}

# --- Ready-to-paste config -------------------------------------------------- #

output "auth_authority" {
  description = "Subscription-infra tfvars value: auth_authority."
  value       = "https://${var.tenant_subdomain}.ciamlogin.com/${var.tenant_id}/"
}

output "auth_audience" {
  description = "Subscription-infra tfvars value: auth_audience (the API client ID)."
  value       = azuread_application.api.client_id
}

output "vite_auth_env" {
  description = "Paste into the SPA build environment (.env / CI) to switch it from dev-auth to OIDC."
  value       = <<-ENV
    VITE_AUTH_AUTHORITY=https://${var.tenant_subdomain}.ciamlogin.com/${var.tenant_id}/
    VITE_AUTH_CLIENT_ID=${azuread_application.spa.client_id}
    VITE_AUTH_SCOPE=api://${azuread_application.api.client_id}/access_as_user
  ENV
}
