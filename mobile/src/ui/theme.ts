export const colors = {
  navy: '#112549',
  cream: '#F4F0E6',
  blue: '#2E6DD8',
  grey: '#6B7280',
} as const;

export const semanticColors = {
  white: '#FFFFFF',
  deepNavy: '#0C1728',
  raisedNavy: '#1D2A3E',
  selectedNavy: '#17366A',
  textOnNavy: '#C6CFDE',
  mutedTextOnNavy: '#AEBBD0',
  faintTextOnNavy: '#9AA8BF',
  linkOnNavy: '#C6D7FF',
  unreadOnNavy: '#BFD0FF',
  mutedCream: '#EAE8E2',
  fieldBorder: '#D1D5DB',
  controlBorderOnNavy: '#71809A',
  subtleSurfaceOnNavy: '#FFFFFF12',
  raisedSurfaceOnNavy: '#FFFFFF18',
  dividerOnNavy: '#FFFFFF20',
  trackOnNavy: '#FFFFFF25',
  softBorderOnNavy: '#FFFFFF2F',
  panelBorderOnNavy: '#FFFFFF30',
  strongBorderOnNavy: '#FFFFFF35',
  warningBackground: '#FEF3C7',
  warningText: '#78350F',
  staleBackground: '#FECACA',
  warning: '#F59E0B',
  success: '#10B981',
  error: '#B42318',
} as const;

export const theme = {
  colors: {
    ...colors,
    ...semanticColors,
  },
  spacing: {
    sm: 8,
    md: 16,
    lg: 24,
  },
} as const;
