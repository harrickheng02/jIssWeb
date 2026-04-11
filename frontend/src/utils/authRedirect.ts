export async function redirectToLogin() {
  const { default: router } = await import('@/router')
  if (router.currentRoute.value.name !== 'auth') {
    await router.push({ name: 'auth' })
  }
}
