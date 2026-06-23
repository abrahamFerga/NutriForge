###############################################################################
# variables.tf
#
# Every value that differs by environment or deployment lives here and is set
# from environments/<env>.tfvars. No secrets belong in tfvars -- secrets are
# created in Key Vault and consumed via Key Vault references / Managed Identity.
###############################################################################

variable "environment" {
  description = "Short environment name (dev, test, prod). Drives naming + tags."
  type        = string
  validation {
    condition     = contains(["dev", "test", "stage", "prod"], var.environment)
    error_message = "environment must be one of: dev, test, stage, prod."
  }
}

variable "location" {
  description = "Azure region for all resources."
  type        = string
  default     = "westeurope"
}

variable "app_name" {
  description = "Base application name used to build resource names (lowercase, no spaces)."
  type        = string
  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,20}$", var.app_name))
    error_message = "app_name must be 2-21 chars, lowercase letters/digits/hyphens, starting with a letter."
  }
}

# --- Container Apps ---------------------------------------------------------- #
variable "acr_sku" {
  description = "Azure Container Registry SKU (Basic for dev, Premium for geo-replication/private endpoints)."
  type        = string
  default     = "Basic"
}

variable "api_image" {
  description = "Container image for the API app. Placeholder on first apply; CD updates it to the ACR image per release."
  type        = string
  default     = "mcr.microsoft.com/k8se/quickstart:latest"
}

variable "api_target_port" {
  description = "Container port the API listens on (ASP.NET Core in containers defaults to 8080)."
  type        = number
  default     = 8080
}

variable "worker_image" {
  description = "Container image for the import worker. Placeholder on first apply; CD updates it per release."
  type        = string
  default     = "mcr.microsoft.com/k8se/quickstart:latest"
}

variable "api_min_replicas" {
  description = "Minimum API replicas. 1 keeps it warm; 0 enables scale-to-zero (cheap dev)."
  type        = number
  default     = 1
}

variable "api_max_replicas" {
  description = "Maximum API replicas under load."
  type        = number
  default     = 3
}

variable "container_cpu" {
  description = "vCPU per container (ACA allows 0.25 increments; cpu and memory must pair)."
  type        = number
  default     = 0.5
}

variable "container_memory" {
  description = "Memory per container, paired with container_cpu (e.g. 0.5 vCPU -> 1Gi)."
  type        = string
  default     = "1Gi"
}

# --- Data tier --------------------------------------------------------------- #
variable "postgres_sku_name" {
  description = "PostgreSQL Flexible Server SKU (e.g. B_Standard_B1ms for dev, GP_Standard_D2s_v3 for prod)."
  type        = string
  default     = "B_Standard_B1ms"
}

variable "postgres_storage_mb" {
  description = "PostgreSQL Flexible Server storage in MB (32768, 65536, 131072, ...)."
  type        = number
  default     = 32768
}

variable "postgres_version" {
  description = "PostgreSQL major version for the Flexible Server."
  type        = string
  default     = "16"
}

variable "postgres_admin_login" {
  description = "Entra ID group/user display name set as the PostgreSQL Entra administrator."
  type        = string
}

variable "postgres_admin_object_id" {
  description = "Object ID of the Entra ID principal that administers PostgreSQL."
  type        = string
}

variable "enable_postgres" {
  description = "Provision PostgreSQL Flexible Server. Set false to skip the data tier (e.g. early prototyping)."
  type        = bool
  default     = true
}

variable "redis_sku_name" {
  description = "Azure Cache for Redis SKU tier (Basic for dev, Standard/Premium for prod HA)."
  type        = string
  default     = "Basic"
}

variable "redis_capacity" {
  description = "Redis cache size unit (Basic/Standard: 0-6 -> C0..C6)."
  type        = number
  default     = 0
}

variable "zone_redundant" {
  description = "Request zone redundancy where the SKU supports it (prod)."
  type        = bool
  default     = false
}

variable "log_retention_days" {
  description = "Log Analytics workspace retention in days."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Extra tags merged onto the standard tag set."
  type        = map(string)
  default     = {}
}
