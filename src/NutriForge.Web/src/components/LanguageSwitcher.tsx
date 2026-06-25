import { useTranslation } from 'react-i18next'

const LANGUAGES = [
  { code: 'en', label: 'EN' },
  { code: 'es', label: 'ES' },
]

export function LanguageSwitcher() {
  const { i18n } = useTranslation()
  const current = i18n.language.slice(0, 2)

  const change = (code: string) => {
    i18n.changeLanguage(code)
    localStorage.setItem('nf:lang', code)
  }

  return (
    <div
      role="radiogroup"
      aria-label="Language"
      className="flex gap-0.5 rounded border border-slate-700 bg-slate-900 p-0.5 text-xs"
    >
      {LANGUAGES.map(({ code, label }) => (
        <button
          key={code}
          role="radio"
          aria-checked={current === code}
          aria-label={code === 'en' ? 'English' : 'Español'}
          onClick={() => change(code)}
          className={[
            'rounded px-2 py-0.5 font-medium transition-colors',
            current === code
              ? 'bg-brand-500 text-white'
              : 'text-slate-400 hover:text-slate-200',
          ].join(' ')}
        >
          {label}
        </button>
      ))}
    </div>
  )
}
