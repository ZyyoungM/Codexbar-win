import type { ChangeEvent } from 'react';

interface InputProps {
  placeholder?: string;
  value?: string;
  defaultValue?: string;
  readOnly?: boolean;
  multiline?: boolean;
  rows?: number;
  className?: string;
  onChange?: (value: string) => void;
  type?: 'text' | 'password';
  theme?: 'light' | 'dark';
  onBlur?: () => void;
}

export function Windows11Input({
  placeholder,
  value,
  defaultValue,
  readOnly = false,
  multiline = false,
  rows = 1,
  className = '',
  onChange,
  type = 'text',
  theme = 'light',
  onBlur
}: InputProps) {
  const isDark = theme === 'dark';
  const baseStyles = 'w-full px-3 rounded border text-[13px] transition-all';
  const interactiveStyles = readOnly
    ? (isDark
      ? 'border-white/10 bg-white/10 text-white/60'
      : 'border-[#0000001a] bg-[#f9f9f9] text-[#605e5c]')
    : (isDark
      ? 'border-white/15 bg-white/10 text-white/90 hover:border-white/25 focus:border-[#60cdff] focus:outline-none focus:ring-1 focus:ring-[#60cdff]'
      : 'border-[#0000001a] bg-white text-[#1c1c1c] hover:border-[#00000033] focus:border-[#0067c0] focus:outline-none focus:ring-1 focus:ring-[#0067c0]');

  const handleChange = (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    onChange?.(event.target.value);
  };

  if (multiline) {
    return (
      <textarea
        className={`${baseStyles} ${interactiveStyles} py-2 resize-none ${className}`}
        placeholder={placeholder}
        defaultValue={defaultValue}
        value={value}
        readOnly={readOnly}
        rows={rows}
        onChange={handleChange}
        onBlur={onBlur}
      />
    );
  }

  return (
    <input
      type={type}
      className={`${baseStyles} ${interactiveStyles} h-8 ${className}`}
      placeholder={placeholder}
      defaultValue={defaultValue}
      value={value}
      readOnly={readOnly}
      onChange={handleChange}
      onBlur={onBlur}
    />
  );
}
