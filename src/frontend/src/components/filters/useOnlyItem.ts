import { useSearchParams } from 'react-router'

/** Task 053n — the URL parameter's value: the one item a link opened the list on ('' = none; see OnlyItemChip). */
export function useOnlyItem(param: string): string {
  const [params] = useSearchParams()
  return params.get(param) ?? ''
}
