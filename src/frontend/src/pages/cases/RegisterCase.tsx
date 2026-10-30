import { RegistrationForm } from '../../components/registration/RegistrationForm'
import { useRegisterCase } from '../../features/cases/api'
import { sq } from '../../i18n/sq'

/**
 * Protokoll registers a new case from a summons (task 021). Fields follow the paper summons; the parties are
 * typed here and sent with the registration. A possible duplicate (409) is shown before registering anyway.
 */
export function RegisterCasePage() {
  const register = useRegisterCase()
  return (
    <RegistrationForm
      mode="new"
      title={sq.cases.register.title}
      send={(request) => register.mutateAsync(request)}
      anotherLabel={sq.cases.register.success.registerAnother}
    />
  )
}
