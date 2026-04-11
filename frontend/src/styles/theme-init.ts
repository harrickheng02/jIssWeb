const key = 'jissweb.theme'
const t = localStorage.getItem(key)
const root = document.documentElement
if (t === 'dark') {
  root.classList.add('dark')
  root.setAttribute('data-theme', 'dark')
} else {
  root.classList.remove('dark')
  root.setAttribute('data-theme', 'light')
}
