/**
 * Test data through the real API (dev authentication: the X-Dev-User header), never through the database and never
 * depending on existing cases. Every case is created fresh, marked "E2E" in its summons number, act number and object
 * (see docs/e2e.md for how they are recognised and cleaned up).
 */
export const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5004'

export const USERS = {
  protokoll: 'protokoll@ash.local',
  kryetar: 'kryetar@ash.local',
  avokat1: 'avokat1@ash.local',
  avokat2: 'avokat2@ash.local',
  monitor: 'monitor@ash.local',
  institucion: 'institucion@ash.local',
} as const

const COURT = 'Gjykata e Shkallës së Parë Tiranë'

export interface CreatedCase {
  id: string
  code: string
  actNumber: string
}

async function call<T>(method: string, path: string, user: string, body?: unknown): Promise<{ status: number; json: T }> {
  const response = await fetch(`${API_URL}${path}`, {
    method,
    headers: { 'X-Dev-User': user, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await response.text()
  return { status: response.status, json: (text ? JSON.parse(text) : null) as T }
}

async function ok<T>(method: string, path: string, user: string, body?: unknown): Promise<T> {
  const { status, json } = await call<T>(method, path, user, body)
  if (status >= 300) throw new Error(`${method} ${path} as ${user} → ${status}: ${JSON.stringify(json)}`)
  return json
}

/** Today in Tirane as yyyy-MM-dd (the registration's received date). */
const tiraneToday = () =>
  new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Tirane', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())

const unique = () => `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`.toUpperCase()

/**
 * Protokoll registers a new case (REGISTERED, office TR) with a natural person as plaintiff and the ministry as
 * defendant — so the ministry monitors it (the Institucionet card and the delegation have something to show).
 */
export async function registerCase(): Promise<CreatedCase> {
  const tag = unique()
  const courts = await ok<{ id: number; name: string }[]>('GET', '/api/reference/courts', USERS.protokoll)
  const court = courts.find((c) => c.name === COURT)
  if (!court) throw new Error(`Court "${COURT}" not found: is the local seed (LocalScripts/903) applied?`)
  const judges = await ok<{ id: number }[]>('GET', `/api/reference/judges?courtId=${court.id}`, USERS.protokoll)
  const ministry = await ok<{ institution: { publicId: string } }>('GET', '/api/identity/me', USERS.institucion)
  const actNumber = `E2E-${tag}`.slice(0, 20)
  const created = await ok<{ casePublicId: string; caseCode: string }>('POST', '/api/cases/register', USERS.protokoll, {
    summonsNumber: `E2E-FL-${tag}`.slice(0, 20),
    actNumber,
    actDate: tiraneToday(),
    receivedDate: tiraneToday(),
    courtLevelCode: 'FIRST_INSTANCE',
    courtId: court.id,
    judgeId: judges[0]?.id,
    objectText: `E2E test ${tag}`,
    firstHearing: { at: new Date(Date.now() + 7 * 24 * 3600 * 1000).toISOString() },
    parties: [
      { partyRoleCode: 'PADITES', kind: 'NATURAL', displayName: `E2E Paditës ${tag}` },
      { partyRoleCode: 'I_PADITUR', kind: 'INSTITUTION', institutionPublicId: ministry.institution.publicId },
    ],
  })
  return { id: created.casePublicId, code: created.caseCode, actNumber }
}

/** The Kryetar assigns the case (it becomes ACTIVE): primary and secondary by their names ("Avokat1 Dev"). */
export async function assignLawyers(caseId: string, primaryName: string, secondaryName: string): Promise<void> {
  const lawyers = await ok<{ publicId: string; fullName: string }[]>(
    'GET', `/api/cases/${caseId}/assignable-lawyers`, USERS.kryetar)
  const id = (name: string) => {
    const found = lawyers.find((l) => l.fullName === name)
    if (!found) throw new Error(`Lawyer "${name}" is not assignable in the case`)
    return found.publicId
  }
  const detail = await ok<{ header: { rowVersion: number } }>('GET', `/api/cases/${caseId}`, USERS.kryetar)
  await ok('POST', `/api/cases/${caseId}/assignment`, USERS.kryetar, {
    primaryUserPublicId: id(primaryName),
    secondaryUserPublicId: id(secondaryName),
    priority: null,
    rowVersion: detail.header.rowVersion,
  })
}

/** permissions[] of GET /api/cases/{id} for the user — null when the user cannot see the case (404). */
export async function permissionsOf(caseId: string, user: string): Promise<string[] | null> {
  const { status, json } = await call<{ permissions: string[] }>('GET', `/api/cases/${caseId}`, user)
  if (status === 404) return null
  if (status !== 200) throw new Error(`GET case as ${user} → ${status}`)
  return json.permissions
}

/** The case's claim as stored (amount as the API's decimal string). */
export async function claimOf(caseId: string, user: string): Promise<string | null> {
  const detail = await ok<{ data: { claim: { amount: string | null } | null } }>('GET', `/api/cases/${caseId}`, user)
  return detail.data.claim?.amount ?? null
}

export async function statusOf(caseId: string, user: string): Promise<string> {
  return (await ok<{ header: { status: string } }>('GET', `/api/cases/${caseId}`, user)).header.status
}

/** Task 040: the user starts with no unread notifications (the bell then counts only what the test causes). */
export async function markAllNotificationsRead(user: string): Promise<void> {
  await ok('POST', '/api/notifications/read-all', user)
}
