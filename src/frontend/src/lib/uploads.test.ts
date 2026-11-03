import { describe, expect, it } from 'vitest'
import { acceptOf, documentForm, extensionOf, fileNameFromDisposition, formatSize, preCheck, titleOf } from './uploads'

const types = [
  { extension: 'pdf', maxSizeBytes: 52428800, appliesTo: 'BOTH' },
  { extension: 'docx', maxSizeBytes: '1048576', appliesTo: 'DOCUMENT' },
  { extension: 'zip', maxSizeBytes: 52428800, appliesTo: 'CHAT' },
]

describe('document uploads (task 035)', () => {
  it('reads the last extension and the default title', () => {
    expect(extensionOf('Vendim.Final.PDF')).toBe('pdf')
    expect(extensionOf('pa-prapashtese')).toBe('')
    expect(extensionOf('.gitignore')).toBe('')
    expect(titleOf('Padia e paditësit.pdf')).toBe('Padia e paditësit')
    expect(titleOf('arkiv.tar.gz')).toBe('arkiv.tar')
  })

  it('pre-checks the extension against DOCUMENT/BOTH types and the size against its limit', () => {
    expect(preCheck({ name: 'a.pdf', size: 1000 }, types)).toBeNull()
    expect(preCheck({ name: 'a.exe', size: 1000 }, types)).toBe('Lloji i skedarit nuk lejohet')
    expect(preCheck({ name: 'a.zip', size: 1000 }, types)).toBe('Lloji i skedarit nuk lejohet') // chat only
    expect(preCheck({ name: 'a.docx', size: 2 * 1024 * 1024 }, types)).toBe('Skedari kalon madhësinë maksimale prej 1 MB.')
    expect(preCheck({ name: 'a.pdf', size: 0 }, types)).toBe('Skedari është bosh.')
  })

  it('accepts only document types in the file picker', () => {
    expect(acceptOf(types)).toBe('.pdf,.docx')
  })

  it('sends the fields before the file', () => {
    const keys = (form: FormData) => {
      const names: string[] = []
      form.forEach((_, name) => names.push(name))
      return names
    }
    const form = documentForm({ documentTypeCode: 'TJETER', otherTypeText: 'Shkresë', title: 'Titulli' }, new Blob(['x']), 'a.pdf')
    expect(keys(form)).toEqual(['documentTypeCode', 'otherTypeText', 'title', 'file'])
    expect(keys(documentForm({ documentTypeCode: 'ANKIM' }, new Blob(['x']), 'a.pdf'))).toEqual(['documentTypeCode', 'file'])
  })

  it('reads the download name, Albanian letters included', () => {
    const header = `attachment; filename="Vendim_gjykate_ceshtja.pdf"; filename*=UTF-8''Vendim_gjykat%C3%AB_%C3%A7%C3%ABshtja.pdf`
    expect(fileNameFromDisposition(header, 'x')).toBe('Vendim_gjykatë_çështja.pdf')
    expect(fileNameFromDisposition('inline; filename="a.pdf"', 'x')).toBe('a.pdf')
    expect(fileNameFromDisposition(null, 'fallback.pdf')).toBe('fallback.pdf')
  })

  it('formats sizes', () => {
    expect(formatSize(512)).toBe('512 B')
    expect(formatSize(850 * 1024)).toBe('850 KB')
    expect(formatSize(2.4 * 1024 * 1024)).toBe('2,4 MB')
  })
})
