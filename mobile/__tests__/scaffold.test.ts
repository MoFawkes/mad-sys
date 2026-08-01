import { colors } from '@/src/ui/theme';

describe('mobile scaffold', () => {
  it('uses the AQI Clock palette', () => {
    expect(colors).toEqual({
      navy: '#112549',
      cream: '#F4F0E6',
      blue: '#2E6DD8',
      grey: '#6B7280',
    });
  });
});
