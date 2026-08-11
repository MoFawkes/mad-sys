import { toInstituteWallClock, wallClockToInstant } from '@/src/time/instituteTime';

describe('institute time', () => {
  it('converts a London lesson wall time to the correct instant', () => {
    const wall = new Date(2026, 7, 11, 17, 0, 0);
    expect(wallClockToInstant(wall, 'Europe/London').toISOString()).toBe('2026-08-11T16:00:00.000Z');
  });

  it('keeps London wall-clock lessons fixed across DST transitions', () => {
    expect(wallClockToInstant(new Date(2026, 2, 29, 17), 'Europe/London').toISOString()).toBe('2026-03-29T16:00:00.000Z');
    expect(wallClockToInstant(new Date(2026, 9, 25, 17), 'Europe/London').toISOString()).toBe('2026-10-25T17:00:00.000Z');
  });

  it('projects an instant into institute wall-clock fields', () => {
    const wall = toInstituteWallClock(new Date('2026-08-11T16:00:00Z'), 'Europe/London');
    expect([wall.getFullYear(), wall.getMonth(), wall.getDate(), wall.getHours()]).toEqual([2026, 7, 11, 17]);
  });
});
