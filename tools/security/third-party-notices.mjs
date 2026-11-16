// Task 051, item 12 — THIRD-PARTY-NOTICES.md: every production NuGet and npm dependency (direct and transitive) with
// its licence, read from the LOCAL package caches only (~/.nuget/packages/*/*.nuspec, src/frontend/node_modules) —
// nothing is downloaded. A licence outside the permissive list is flagged.
//   node tools/security/third-party-notices.mjs
// (restore / npm install first, so the caches hold every package.)

import { execSync } from 'node:child_process'
import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { homedir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')

/** Licences that ask only for attribution (no copyleft, no field-of-use limits). */
const PERMISSIVE = new Set([
  'MIT', 'MIT-0', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause', 'ISC', '0BSD', 'Unlicense', 'CC0-1.0', 'Zlib',
  'BlueOak-1.0.0', 'Python-2.0', 'MS-PL', 'PostgreSQL', 'CC-BY-4.0',
])

/** A licence expression is permissive when one of its OR-alternatives is entirely permissive. */
function permissive(expression) {
  if (!expression) return false
  return expression
    .replace(/[()]/g, '')
    .split(/\s+OR\s+/i)
    .some((alt) => alt.split(/\s+AND\s+/i).every((id) => PERMISSIVE.has(id.trim())))
}

// ---------------------------------------------------------------- NuGet (production projects only)
function nugetPackages() {
  const json = execSync('dotnet list Ash.sln package --include-transitive --format json', {
    cwd: join(root, 'src', 'backend'),
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  })
  const report = JSON.parse(json)
  const packages = new Map()
  for (const project of report.projects) {
    if (/[\\/]Tests[\\/]/.test(project.path)) continue // test projects ship nothing
    for (const framework of project.frameworks ?? []) {
      for (const p of [...(framework.topLevelPackages ?? []), ...(framework.transitivePackages ?? [])]) {
        packages.set(`${p.id}@${p.resolvedVersion}`, { id: p.id, version: p.resolvedVersion })
      }
    }
  }
  const cache = process.env.NUGET_PACKAGES ?? join(homedir(), '.nuget', 'packages')
  return [...packages.values()].map(({ id, version }) => {
    const nuspec = join(cache, id.toLowerCase(), version.toLowerCase(), `${id.toLowerCase()}.nuspec`)
    let license = null
    let url = null
    if (existsSync(nuspec)) {
      const xml = readFileSync(nuspec, 'utf8')
      license = xml.match(/<license[^>]*type="expression"[^>]*>([^<]+)<\/license>/)?.[1]?.trim() ?? null
      if (!license && /<license[^>]*type="file"/.test(xml)) license = 'file (see package)'
      url = xml.match(/<licenseUrl>([^<]+)<\/licenseUrl>/)?.[1]?.trim() ?? null
      // the old Microsoft packages only link to the MIT licence text
      if (!license && url && /licenses\/MIT|mit-license|\/LICENSE\.TXT$/i.test(url) && /microsoft|dotnet/i.test(url)) license = 'MIT'
      // only a link to a licence file elsewhere: not read (no download) — listed for review with the link
      if (!license && url) license = 'see licence URL'
    }
    return { name: id, version, license, url, source: 'NuGet' }
  })
}

// ---------------------------------------------------------------- npm (production dependencies only)
function npmPackages() {
  const frontend = join(root, 'src', 'frontend')
  const tree = JSON.parse(
    // --long: every node carries its own path (a nested copy has another version than the top-level one)
    execSync('npm ls --omit=dev --all --json --long', { cwd: frontend, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }),
  )
  const packages = new Map()
  const walk = (deps) => {
    for (const [name, info] of Object.entries(deps ?? {})) {
      if (!info.version) continue
      const key = `${name}@${info.version}`
      if (!packages.has(key)) {
        const manifest = join(info.path ?? join(frontend, 'node_modules', name), 'package.json')
        let license = null
        if (existsSync(manifest)) {
          const pkg = JSON.parse(readFileSync(manifest, 'utf8'))
          license = typeof pkg.license === 'string' ? pkg.license : pkg.license?.type ?? pkg.licenses?.map((l) => l.type).join(' OR ') ?? null
        }
        packages.set(key, { name, version: info.version, license, url: null, source: 'npm' })
      }
      walk(info.dependencies)
    }
  }
  walk(tree.dependencies)
  return [...packages.values()]
}

const all = [...nugetPackages(), ...npmPackages()].sort((a, b) =>
  a.source === b.source ? a.name.localeCompare(b.name) : a.source.localeCompare(b.source),
)
const flagged = all.filter((p) => !permissive(p.license))

const rows = (source) =>
  all
    .filter((p) => p.source === source)
    .map((p) => `| ${p.name} | ${p.version} | ${p.license ?? 'unknown'}${permissive(p.license) ? '' : ' ⚠'} |`)
    .join('\n')

const md = `# Third-party notices

The production dependencies of the system (NuGet: the backend's non-test projects; npm: the frontend without dev
dependencies), direct and transitive, with their licences — generated by \`node tools/security/third-party-notices.mjs\`
from the local package caches (task 051). ⚠ = not on the permissive list (MIT, Apache-2.0, BSD, ISC, …): to be
reviewed.

## Review needed (${flagged.length})

${flagged.length === 0 ? 'None.' : flagged.map((p) => `- **${p.name} ${p.version}** (${p.source}): ${p.license ?? 'licence not found in the package'}${p.url ? ` — ${p.url}` : ''}`).join('\n')}

## NuGet (${all.filter((p) => p.source === 'NuGet').length})

| Package | Version | Licence |
|---|---|---|
${rows('NuGet')}

## npm (${all.filter((p) => p.source === 'npm').length})

| Package | Version | Licence |
|---|---|---|
${rows('npm')}
`

writeFileSync(join(root, 'THIRD-PARTY-NOTICES.md'), md)
console.log(`THIRD-PARTY-NOTICES.md: ${all.length} packages, ${flagged.length} to review`)
for (const p of flagged) console.log(`  REVIEW ${p.source} ${p.name} ${p.version}: ${p.license ?? 'unknown'}`)
