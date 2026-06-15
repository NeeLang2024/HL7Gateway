const OFFSET_PATTERN = /(?:z|[+-]\d{2}:?\d{2})$/i

export function formatDateTime(value?: string | Date | null): string {
  if (!value) return '-'

  if (typeof value === 'string') {
    const trimmed = value.trim()
    if (!trimmed) return '-'

    if (!OFFSET_PATTERN.test(trimmed)) {
      return trimmed.replace('T', ' ').slice(0, 19)
    }
  }

  const date = value instanceof Date ? value : new Date(value)
  if (Number.isNaN(date.getTime())) {
    return String(value).replace('T', ' ').slice(0, 19)
  }

  const parts = new Intl.DateTimeFormat('zh-CN', {
    timeZone: 'Asia/Shanghai',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).formatToParts(date)

  const part = (type: string) => parts.find((item) => item.type === type)?.value ?? '00'
  return `${part('year')}-${part('month')}-${part('day')} ${part('hour')}:${part('minute')}:${part('second')}`
}
