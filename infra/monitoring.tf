###############################################################################
# monitoring.tf
#
# Azure Monitor workbook and SLO-based alert rules for NutriForge (#51).
# Alerts are implemented as Scheduled Query Rules (Log Analytics / App Insights)
# with the burn-rate thresholds from docs/sre/slos.yaml.
#
# Prerequisites: the Log Analytics workspace and Application Insights resources
# declared in main.tf must already exist.
###############################################################################

locals {
  appinsights_resource_id  = azurerm_application_insights.main.id
  log_analytics_resource_id = azurerm_log_analytics_workspace.main.id
}

# ── Workbook ────────────────────────────────────────────────────────────────── #

resource "azurerm_application_insights_workbook" "nutriforge_ops" {
  name                = "44444444-4444-4444-4444-${local.name_suffix}444444"  # deterministic GUID prefix
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  display_name        = "NutriForge Operations"
  source_id           = local.appinsights_resource_id
  tags                = local.tags

  data_json = templatefile("${path.module}/workbook-template.json", {
    subscription_id    = data.azurerm_client_config.current.subscription_id
    resource_group     = azurerm_resource_group.main.name
    app_insights_name  = azurerm_application_insights.main.name
    app_insights_id    = local.appinsights_resource_id
    log_workspace_id   = local.log_analytics_resource_id
  })
}

# ── Action group: page + ticket ─────────────────────────────────────────────── #

resource "azurerm_monitor_action_group" "slo_critical" {
  name                = "ag-${local.base}-slo-critical"
  resource_group_name = azurerm_resource_group.main.name
  short_name          = "slo-crit"
  tags                = local.tags

  # Webhook target: override with a real PagerDuty / OpsGenie endpoint.
  webhook_receiver {
    name                    = "oncall-webhook"
    service_uri             = var.oncall_webhook_url
    use_common_alert_schema = true
  }
}

resource "azurerm_monitor_action_group" "slo_warning" {
  name                = "ag-${local.base}-slo-warning"
  resource_group_name = azurerm_resource_group.main.name
  short_name          = "slo-warn"
  tags                = local.tags

  email_receiver {
    name          = "engineering-team"
    email_address = var.engineering_email
  }
}

# ── Alert: API availability — fast-burn (5× rate, double trigger) ───────────── #

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "availability_fast_burn" {
  name                = "slo-availability-fast-burn-${local.base}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  description         = "API availability SLO fast-burn: 5× error-budget burn rate (double-window trigger). Page on-call."
  severity            = 0 # Critical
  enabled             = true
  tags                = local.tags

  scopes                  = [local.appinsights_resource_id]
  evaluation_frequency    = "PT5M"
  window_duration         = "PT1H"
  auto_mitigation_enabled = true

  criteria {
    query = <<-KQL
      requests
      | where timestamp > ago(1h)
      | summarize
          total   = count(),
          bad     = countif(resultCode startswith "5")
      | extend error_rate   = toreal(bad) / toreal(total)
      | extend budget_rate  = 1.0 - 0.995          // SLO target
      | where error_rate > (5.0 * budget_rate)      // 5× burn rate
    KQL
    time_aggregation_method = "Count"
    operator                = "GreaterThan"
    threshold               = 0
    metric_measure_column   = null
    resource_id_column      = null

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 2
      number_of_evaluation_periods             = 2
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.slo_critical.id]
  }
}

# ── Alert: API availability — slow-burn (2× rate, 6 h window) ───────────────── #

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "availability_slow_burn" {
  name                = "slo-availability-slow-burn-${local.base}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  description         = "API availability SLO slow-burn: 2× error-budget burn rate over 6 h. Create ticket."
  severity            = 2 # Warning
  enabled             = true
  tags                = local.tags

  scopes                  = [local.appinsights_resource_id]
  evaluation_frequency    = "PT15M"
  window_duration         = "PT6H"
  auto_mitigation_enabled = true

  criteria {
    query = <<-KQL
      requests
      | where timestamp > ago(6h)
      | summarize
          total = count(),
          bad   = countif(resultCode startswith "5")
      | extend error_rate  = toreal(bad) / toreal(total)
      | extend budget_rate = 1.0 - 0.995
      | where error_rate > (2.0 * budget_rate)
    KQL
    time_aggregation_method = "Count"
    operator                = "GreaterThan"
    threshold               = 0
    metric_measure_column   = null
    resource_id_column      = null

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 1
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.slo_warning.id]
  }
}

# ── Alert: API latency p99 > 2 s (fast-burn) ────────────────────────────────── #

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "latency_p99_fast_burn" {
  name                = "slo-latency-p99-fast-burn-${local.base}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  description         = "API latency p99 SLO breach: non-AI requests > 2 s at 5× burn rate. Page on-call."
  severity            = 1 # Error
  enabled             = true
  tags                = local.tags

  scopes                  = [local.appinsights_resource_id]
  evaluation_frequency    = "PT5M"
  window_duration         = "PT1H"
  auto_mitigation_enabled = true

  criteria {
    query = <<-KQL
      requests
      | where timestamp > ago(1h)
      | where url !has "/diet-plan/generate"
            and url !has "/assistant/"
      | summarize
          total  = count(),
          slow   = countif(duration > 2000)  // 2 s in ms (App Insights unit)
      | extend slow_rate   = toreal(slow) / toreal(total)
      | extend budget_rate = 1.0 - 0.99
      | where slow_rate > (5.0 * budget_rate)
    KQL
    time_aggregation_method = "Count"
    operator                = "GreaterThan"
    threshold               = 0
    metric_measure_column   = null
    resource_id_column      = null

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 2
      number_of_evaluation_periods             = 2
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.slo_critical.id]
  }
}

# ── Alert: plan generation latency p95 > 45 s ───────────────────────────────── #

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "plan_latency_p95" {
  name                = "slo-plan-latency-p95-${local.base}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  description         = "Diet plan generation p95 latency SLO breach: > 45 s sustained. Create ticket."
  severity            = 2
  enabled             = true
  tags                = local.tags

  scopes                  = [local.log_analytics_resource_id]
  evaluation_frequency    = "PT15M"
  window_duration         = "PT1H"
  auto_mitigation_enabled = true

  criteria {
    query = <<-KQL
      customMetrics
      | where timestamp > ago(1h)
      | where name == "nutriforge.plan.generation_duration"
      | summarize p95 = percentile(value, 95)
      | where p95 > 45
    KQL
    time_aggregation_method = "Count"
    operator                = "GreaterThan"
    threshold               = 0
    metric_measure_column   = null
    resource_id_column      = null

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 3
      number_of_evaluation_periods             = 4
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.slo_warning.id]
  }
}

# ── Alert: plan feasibility rate < 90 % ─────────────────────────────────────── #

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "plan_feasibility" {
  name                = "slo-plan-feasibility-${local.base}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  description         = "Plan feasibility SLO: fewer than 90 % of plans marked feasible=true. Page on-call."
  severity            = 1
  enabled             = true
  tags                = local.tags

  scopes                  = [local.log_analytics_resource_id]
  evaluation_frequency    = "PT30M"
  window_duration         = "PT6H"
  auto_mitigation_enabled = true

  criteria {
    query = <<-KQL
      customMetrics
      | where timestamp > ago(6h)
      | where name == "nutriforge.plan.generated"
      | summarize
          total    = sum(value),
          feasible = sumif(value, tostring(customDimensions.feasible) == "true")
      | where total > 0
      | extend feasibility_rate = toreal(feasible) / toreal(total)
      | where feasibility_rate < 0.90
    KQL
    time_aggregation_method = "Count"
    operator                = "GreaterThan"
    threshold               = 0
    metric_measure_column   = null
    resource_id_column      = null

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 2
      number_of_evaluation_periods             = 2
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.slo_critical.id]
  }
}

# ── Alert: AI token budget exhaustion rate ───────────────────────────────────── #

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "ai_budget_exhaustion" {
  name                = "alert-ai-budget-exhaustion-${local.base}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  description         = "More than 10 % of assistant turns rejected due to monthly token budget exhaustion."
  severity            = 2
  enabled             = true
  tags                = local.tags

  scopes                  = [local.log_analytics_resource_id]
  evaluation_frequency    = "PT30M"
  window_duration         = "PT1H"
  auto_mitigation_enabled = true

  criteria {
    query = <<-KQL
      customMetrics
      | where timestamp > ago(1h)
      | where name == "nutriforge.ai.turns"
      | summarize
          total    = sum(value),
          rejected = sumif(value, tostring(customDimensions.outcome) == "budget_exceeded")
      | where total > 5   // ignore noise when usage is very low
      | extend rejection_rate = toreal(rejected) / toreal(total)
      | where rejection_rate > 0.10
    KQL
    time_aggregation_method = "Count"
    operator                = "GreaterThan"
    threshold               = 0
    metric_measure_column   = null
    resource_id_column      = null

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 2
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.slo_warning.id]
  }
}
