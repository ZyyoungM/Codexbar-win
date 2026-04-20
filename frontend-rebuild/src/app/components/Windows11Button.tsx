interface ButtonProps {
  children: React.ReactNode;
  variant?: 'primary' | 'secondary' | 'subtle' | 'danger';
  size?: 'sm' | 'md';
  className?: string;
  onClick?: () => void;
  theme?: 'light' | 'dark';
  disabled?: boolean;
  type?: 'button' | 'submit';
}

export function Windows11Button({
  children,
  variant = 'secondary',
  size = 'md',
  className = '',
  onClick,
  theme = 'light',
  disabled = false,
  type = 'button'
}: ButtonProps) {
  const isDark = theme === 'dark';
  const baseStyles = 'inline-flex items-center justify-center rounded transition-all cursor-default select-none disabled:opacity-50 disabled:pointer-events-none';

  const sizeStyles = {
    sm: 'px-3 h-6 text-[12px]',
    md: 'px-4 h-8 text-[13px]'
  };

  const variantStylesLight = {
    primary: 'bg-[#0067c0] text-white hover:bg-[#005a9e] active:bg-[#004578]',
    secondary: 'bg-[#f3f3f3] text-[#1c1c1c] border border-[#0000001a] hover:bg-[#f9f9f9] active:bg-[#efefef]',
    subtle: 'bg-transparent text-[#1c1c1c] hover:bg-[#f3f3f3] active:bg-[#e9e9e9]',
    danger: 'bg-[#c42b1c] text-white hover:bg-[#a52416] active:bg-[#881f12]'
  };

  const variantStylesDark = {
    primary: 'bg-[#60cdff] text-[#1c1c1c] hover:bg-[#3aa0f3] active:bg-[#1890e3]',
    secondary: 'bg-white/10 text-white border border-white/10 hover:bg-white/15 active:bg-white/20',
    subtle: 'bg-transparent text-white/80 hover:bg-white/10 active:bg-white/15',
    danger: 'bg-[#c42b1c] text-white hover:bg-[#a52416] active:bg-[#881f12]'
  };

  const variantStyles = isDark ? variantStylesDark : variantStylesLight;

  return (
    <button
      type={type}
      className={`${baseStyles} ${sizeStyles[size]} ${variantStyles[variant]} ${className}`}
      onClick={onClick}
      disabled={disabled}
    >
      {children}
    </button>
  );
}
