export interface PackageSummary {
  packageId: string
  name: string
  packageVersion: string
  publisher: string
  state: string
  statusDetail: string | null
  entities: number
  rules: number
  events: number
  capabilities: number
}

export interface AttributeDeclaration {
  name: string
  dataType: string
  required: boolean
  description?: string | null
}

export interface EntityDeclaration {
  entityTypeName: string
  description?: string | null
  attributes: AttributeDeclaration[]
}

export interface EventDeclaration {
  eventName: string
  description?: string | null
  payloadFields: AttributeDeclaration[]
}

export interface RuleCondition {
  field: string
  operator: string
  value: string
}

export interface RuleDeclaration {
  ruleId: string
  name: string
  description?: string | null
  triggerEvent: string
  priority: number
  conditions: RuleCondition[]
  outcomeOnMatch: string
  reasonTemplate: string
}

export interface CapabilityDeclaration {
  capabilityId: string
  name: string
  description?: string | null
}

export interface Manifest {
  packageId: string
  name: string
  packageVersion: string
  publisher: string
  description?: string | null
  dependencies: { packageId: string; minimumVersion: string }[]
  entities: EntityDeclaration[]
  events: EventDeclaration[]
  rules: RuleDeclaration[]
  capabilities: CapabilityDeclaration[]
  // Optional declaration kinds — present in the full manifest JSON but not on
  // every package; rendered by the inspector only when they exist.
  knowledgeAssets?: { assetId: string; title?: string | null; summary?: string | null; category?: string | null }[]
  agents?: { agentId: string; name?: string | null; description?: string | null }[]
  performanceFrameworks?: {
    frameworkId: string
    name?: string | null
    kpis?: { kpiId: string; name?: string | null; description?: string | null }[]
  }[]
}

export interface PackageDetail {
  state: string
  statusDetail: string | null
  manifest: Manifest
}

export interface OutcomeInfo {
  outcome: string
  reason: string
  ruleId: string | null
  packageId: string | null
  packageVersion: string | null
  eventName: string
  processedAt: string
}

export interface ActivityItem {
  unitRef?: string | null
  recordId: string
  recordType: string
  submittedBy: string
  receivedAt: string
  payload: Record<string, string>
  outcomes: OutcomeInfo[]
}

export interface ScoreRecord {
  unitRef?: string | null
  id: string
  subjectType: string
  subjectId: string
  scoreType: string
  value: number
  ruleId: string | null
  packageId: string | null
  packageVersion: string | null
  riskType?: string | null
  metadata?: Record<string, string>
  correlationId: string
  computedAt: string
}

export interface AuditEntryDto {
  id: string
  correlationId: string
  category: string
  summary: string
  detail: Record<string, string>
  occurredAt: string
}

export interface OperationResult {
  success: boolean
  state: string | null
  errors: string[]
}
