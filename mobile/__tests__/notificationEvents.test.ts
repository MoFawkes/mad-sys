import { getNotificationEvents } from '@/src/domain/scheduleEngine';

import { id, monday, normalDay, override, period, timetable, tuesday, weekOf } from '../test/testData';

describe('getNotificationEvents', () => {
  it('gives every period a start event including breaks', () => {
    const names = getNotificationEvents(weekOf(normalDay()), monday, 5)
      .filter((event) => event.kind === 'start')
      .map((event) => event.occurrence.period.name);
    expect(names).toEqual(['Period 1', 'Break', 'Period 2']);
  });
  it('places the end warning lead minutes before the end', () => {
    const warning = getNotificationEvents(weekOf(normalDay()), monday, 5).find(
      (event) => event.kind === 'end-warning' && event.occurrence.period.name === 'Period 1',
    );
    expect(warning?.triggerTime).toEqual(new Date(2026, 6, 13, 9, 55));
  });
  it('suppresses warnings for periods no longer than the lead', () => {
    const item = timetable(
      'Short blocks',
      period('Five', '09:00', '09:05', 1),
      period('Four', '09:10', '09:14', 2),
      period('Six', '09:20', '09:26', 3),
    );
    expect(
      getNotificationEvents(weekOf(item), monday, 5)
        .filter((event) => event.kind === 'end-warning')
        .map((event) => event.occurrence.period.name),
    ).toEqual(['Six']);
  });
  it('disables every end warning when lead is zero', () => {
    const events = getNotificationEvents(weekOf(normalDay()), monday, 0);
    expect(events).toHaveLength(3);
    expect(events.every((event) => event.kind === 'start')).toBe(true);
  });
  it('rejects a negative lead', () => {
    expect(() => getNotificationEvents(weekOf(normalDay()), monday, -1)).toThrow(RangeError);
  });
  it('returns no events for a closed day', () => {
    expect(getNotificationEvents(weekOf(normalDay(), override(monday, null)), monday, 5)).toEqual([]);
  });
  it('orders events by trigger time', () => {
    const events = getNotificationEvents(weekOf(normalDay()), monday, 5);
    expect(events.map((event) => event.triggerTime.getTime())).toEqual(
      events.map((event) => event.triggerTime.getTime()).sort((a, b) => a - b),
    );
  });
  it('uses the byte-stable lowercase dashless and date-scoped key format', () => {
    const periodId = 'A0B1C2D3-E4F5-4678-9ABC-DEF012345678';
    const item = timetable('One', period('Only', '09:00', '10:00', 0, true, periodId));
    const events = getNotificationEvents(weekOf(item), monday, 5);
    expect(events[0].key).toBe('start:a0b1c2d3e4f546789abcdef012345678:2026-07-13');
    expect(events[1].key).toBe('end-warning:a0b1c2d3e4f546789abcdef012345678:2026-07-13');
  });
  it('uses different keys for the same period on different dates', () => {
    const snapshot = weekOf(normalDay());
    expect(getNotificationEvents(snapshot, monday, 5)[0].key).not.toBe(
      getNotificationEvents(snapshot, tuesday, 5)[0].key,
    );
  });
  it('creates events for both overlapping periods', () => {
    const item = timetable(
      'Overlap',
      period('Long block', '09:00', '12:00', 1, true, id()),
      period('Intervention', '10:00', '10:30', 2, true, id()),
    );
    expect(getNotificationEvents(weekOf(item), monday, 5)).toHaveLength(4);
  });
  it('breaks identical trigger times by ordinal key', () => {
    const item = timetable(
      'Tie',
      period('Second id', '09:00', '10:00', 1, true, 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'),
      period('First id', '09:00', '10:00', 2, true, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'),
    );
    const starts = getNotificationEvents(weekOf(item), monday, 0);
    expect(starts.map((event) => event.key)).toEqual([
      'start:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:2026-07-13',
      'start:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb:2026-07-13',
    ]);
  });
});
