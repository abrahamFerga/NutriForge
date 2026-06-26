###############################################################################
# infra/entra/variables.tf
#
# Inputs for the Entra External ID app registrations. No secrets here — app
# registrations don't need client secrets (the SPA is a public client using
# Auth-Code + PKCE, the API only validates tokens).
###############################################################################

variable "tenant_id" {
  description = "Entra External ID (CIAM) tenant GUID the app registrations are created in."
  type        = string
  validation {
    condition     = can(regex("^[0-9a-fA-F-]{36}$", var.tenant_id))
    error_message = "tenant_id must be a GUID (the CIAM tenant's directory/tenant ID)."
  }
}

variable "tenant_subdomain" {
  description = "CIAM tenant subdomain (the part before .ciamlogin.com / .onmicrosoft.com), e.g. \"nutriforge\" for nutriforge.ciamlogin.com."
  type        = string
  validation {
    condition     = can(regex("^[a-z0-9-]{1,63}$", var.tenant_subdomain))
    error_message = "tenant_subdomain must be lowercase letters/digits/hyphens (no domain suffix)."
  }
}

variable "app_name" {
  description = "Base name used to build the app-registration display names (lowercase, no spaces)."
  type        = string
  default     = "nutriforge"
  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,20}$", var.app_name))
    error_message = "app_name must be 2-21 chars, lowercase letters/digits/hyphens, starting with a letter."
  }
}

variable "spa_redirect_uris" {
  description = <<-DESC
    Allowed SPA redirect URIs (Auth-Code + PKCE). MSAL redirects back to
    window.location.origin, so list every origin the SPA is served from —
    the deployed site plus local dev. Example:
      ["https://nutriforge.example.com", "http://localhost:5173"]
  DESC
  type        = list(string)
  validation {
    condition     = length(var.spa_redirect_uris) > 0
    error_message = "Provide at least one SPA redirect URI (e.g. the deployed origin)."
  }
}

variable "admin_principal_object_ids" {
  description = <<-DESC
    Object IDs of Entra users/groups to grant the API's `admin` app role
    (maps to the RequireRole(admin) RBAC policy). Optional — leave empty and
    assign the role in the portal later. Prefer a group object ID.
  DESC
  type        = list(string)
  default     = []
}
