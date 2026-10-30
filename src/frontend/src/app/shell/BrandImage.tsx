import { Box, type SxProps, type Theme } from '@mui/material'
import { useState, type ReactNode } from 'react'

/**
 * An optional brand image from public/brand. Renders NOTHING (and not `wrap` either) when the file is missing or
 * cannot be shown — no placeholder drawing. `decorative`: alt="" and aria-hidden, so screen readers ignore it.
 */
export function BrandImage({
  src,
  alt,
  sx,
  wrap,
  decorative,
}: {
  src: string
  alt: string
  sx?: SxProps<Theme>
  /** e.g. a divider that only makes sense next to the image */
  wrap?: (image: ReactNode) => ReactNode
  decorative?: boolean
}) {
  const [failed, setFailed] = useState(false)
  if (failed) return null
  const image = (
    <Box
      component="img"
      src={src}
      alt={decorative ? '' : alt}
      aria-hidden={decorative ? true : undefined}
      onError={() => setFailed(true)}
      sx={sx}
    />
  )
  return <>{wrap ? wrap(image) : image}</>
}
