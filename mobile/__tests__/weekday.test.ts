import { jsDayToDbWeekday } from '@/src/domain/weekday';

import { localDate } from '../test/testData';

describe('jsDayToDbWeekday', () => {
  it.each([
    [localDate(2026, 7, 13), 0],
    [localDate(2026, 7, 14), 1],
    [localDate(2026, 7, 15), 2],
    [localDate(2026, 7, 16), 3],
    [localDate(2026, 7, 17), 4],
    [localDate(2026, 7, 18), 5],
    [localDate(2026, 7, 19), 6],
  ])('maps %s to database weekday %i', (date, expected) => {
    expect(jsDayToDbWeekday(date)).toBe(expected);
  });
});
