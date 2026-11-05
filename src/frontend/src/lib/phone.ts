/**
 * Shows an E.164 phone as stored by the API (task 048) in groups: an Albanian mobile "+355 69 123 4567", an Albanian
 * landline "+355 4 222 3333"; any other number stays as stored.
 */
export function formatPhone(phone: string): string {
  const mobile = /^\+355(6\d)(\d{3})(\d{4})$/.exec(phone)
  if (mobile) return `+355 ${mobile[1]} ${mobile[2]} ${mobile[3]}`
  const landline = /^\+355(4)(\d{3})(\d{4})$/.exec(phone)
  if (landline) return `+355 ${landline[1]} ${landline[2]} ${landline[3]}`
  return phone
}
