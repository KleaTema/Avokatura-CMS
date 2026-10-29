/** Menu codes come from `me.menus`; codes without an entry here go to `/<code>` (a placeholder until built). */
const menuPaths: Record<string, string> = {
  admin: '/admin',
}

export const menuPath = (code: string) => menuPaths[code] ?? `/${code}`
