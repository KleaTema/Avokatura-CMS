import { z } from 'zod'

/**
 * Task 051: zod would probe for eval (Function('')) to compile its validators, and the CSP forbids eval
 * (script-src 'self'). Imported FIRST in main.tsx: modules run in import order, so this must run before any module
 * that defines a schema.
 */
z.config({ jitless: true })
