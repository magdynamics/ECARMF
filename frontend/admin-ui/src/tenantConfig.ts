// Tenant-Aware Shell — the TenantConfig injection layer (ECARMF-ADR-UIUX-001
// Phase 1, §2.1). One application shell serves every tenant; per-tenant
// branding, terminology, accent, and posture flags are injected here rather
// than forked into per-tenant UI. A tenant with no entry falls back to the
// platform default, so onboarding a new tenant never requires a code change
// unless it wants custom branding.

export interface TenantConfig {
  /** Display name shown in the shell for this tenant's workspace. */
  brand: string
  /** Short industry/segment label surfaced next to the brand. */
  segment?: string
  /** Accent colour (CSS colour) applied via a CSS variable on the shell. */
  accent: string
  /** Regulated-data posture: HIPAA/PHI or securities. Drives the masked-field
   *  pattern (§2.4) and stricter defaults. Mirrors the backend SensitivityTier;
   *  the UI treats anything at or above `regulated` as PHI-capable. */
  posture: 'standard' | 'elevated' | 'regulated'
  /** True when this tenant handles PHI and every PHI-badged field must default
   *  to masked with an audit-logged reveal (§2.4). */
  phi: boolean
  /** Domain-specific relabelling of generic shell terms. Keys are stable
   *  internal terms; values are what THIS tenant calls them. */
  terms?: Partial<Record<TermKey, string>>
}

/** Generic shell terms a tenant may rename (progressive disclosure, risk, etc.). */
export type TermKey =
  | 'control'
  | 'controls'
  | 'risk'
  | 'record'
  | 'manager'
  | 'unit'

const DEFAULT: TenantConfig = {
  brand: 'ECARMF',
  accent: '#5aa9e6',
  posture: 'standard',
  phi: false,
}

// Per-tenant overrides. Only what differs from DEFAULT needs stating.
const REGISTRY: Record<string, TenantConfig> = {
  platform: {
    brand: 'ECARMF',
    segment: 'Operator Console',
    accent: '#5aa9e6',
    posture: 'standard',
    phi: false,
  },
  'tenant-10': {
    brand: 'Tenant-10 RCM',
    segment: 'Medical Billing · Revenue-Cycle Management',
    accent: '#2fbf9f',
    posture: 'regulated',
    phi: true, // first HIPAA-scoped tenant — PHI masking is mandatory
    terms: { record: 'claim', unit: 'department' },
  },
  tcel: {
    brand: 'TCEL',
    segment: 'Multi-Manager Trading & Treasury',
    accent: '#c69749',
    posture: 'regulated', // securities-regulated
    phi: false,
    terms: { unit: 'legal entity', manager: 'manager' },
  },
}

/** Resolve the config for a tenant id, falling back to the platform default. */
export function tenantConfig(tenantId: string | null | undefined): TenantConfig {
  if (!tenantId) return DEFAULT
  return REGISTRY[tenantId.toLowerCase()] ?? { ...DEFAULT, brand: tenantId }
}

/** Look up a possibly-renamed term for a tenant (falls back to the default word). */
export function term(cfg: TenantConfig, key: TermKey, fallback: string): string {
  return cfg.terms?.[key] ?? fallback
}
