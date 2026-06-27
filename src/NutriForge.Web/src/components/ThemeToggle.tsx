import { Moon, Sun, Monitor } from 'lucide-react'
import { useTheme } from './ThemeProvider'

const OPTIONS = [
  { value: 'light' as const, label: 'Light', icon: Sun },
  { value: 'dark' as const, label: 'Dark', icon: Moon },
  { value: 'system' as const, label: 'System', icon: Monitor },
]

export function ThemeToggle() {
  const { theme, setTheme } = useTheme()

  return (
    <div
      role="radiogroup"
      aria-label="Color theme"
      className="flex gap-1 rounded-lg border border-slate-700 bg-slate-900 p-0.5"
    >
      {OPTIONS.map(({ value, label, icon: Icon }) => (
        <button
          key={value}
          role="radio"
          aria-checked={theme === value}
          aria-label={label}
          onClick={() => setTheme(value)}
          className={[
            'flex items-center gap-1.5 rounded-md px-2 py-1.5 text-xs font-medium transition-colors sm:px-2.5 sm:py-1',
            theme === value
              ? 'bg-brand-500 text-white shadow-sm'
              : 'text-slate-400 hover:text-slate-200',
          ].join(' ')}
        >
          <Icon className="h-3.5 w-3.5" aria-hidden="true" />
          {/* Labels would overflow the mobile top bar — icon-only on phones (the aria-label keeps it accessible). */}
          <span className="hidden sm:inline">{label}</span>
        </button>
      ))}
    </div>
  )
}
