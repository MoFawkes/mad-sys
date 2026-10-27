import { groupPeriods } from '@/src/ui/periodGroups';
import { period } from '../test/testData';

describe('today period rows', () => {
  it('combines simultaneous sessions while keeping each lesson and class', () => {
    const first = { period: period('Lesson 1', '18:15', '18:40'), classId: 'session-3' };
    const second = { period: period('Lesson 2', '18:15:00', '18:40:00'), classId: 'session-2' };
    const third = { period: period('Lesson 4', '18:15', '18:40'), classId: 'session-1' };
    const groups = groupPeriods([first, second, third]);
    expect(groups).toHaveLength(1);
    expect(groups[0].periods).toEqual([third, second, first]);
    expect(groups[0].sharedName).toBeNull();
  });

  it('shows one shared Naseehah heading while retaining every session', () => {
    const entries = ['session-3', 'session-2', 'session-1'].map((classId) => ({
      period: period('Isha + Naseehah', '18:40', '19:05'), classId,
    }));
    const [group] = groupPeriods(entries);
    expect(group.sharedName).toBe('Isha + Naseehah');
    expect(group.periods.map((item) => item.classId)).toEqual(['session-1', 'session-2', 'session-3']);
  });

  it('uses the same configured session order in every time slot', () => {
    const entries = [
      { period: period('Lesson 1', '18:15', '18:40'), classId: 'a' },
      { period: period('Lesson 2', '18:15', '18:40'), classId: 'b' },
      { period: period('Isha', '18:40', '19:05'), classId: 'b' },
      { period: period('Isha', '18:40', '19:05'), classId: 'a' },
    ];
    const groups = groupPeriods(entries, new Map([['b', 0], ['a', 1]]));
    expect(groups.map((group) => group.periods.map((item) => item.classId))).toEqual([['b', 'a'], ['b', 'a']]);
  });

  it('keeps matching lesson names with different starts or ends in separate rows', () => {
    const entries = [
      { period: period('Lesson 1', '18:15', '18:40'), classId: 'a' },
      { period: period('Lesson 1', '18:15', '19:05'), classId: 'b' },
      { period: period('Lesson 1', '18:40', '19:05'), classId: 'c' },
    ];
    expect(groupPeriods(entries)).toHaveLength(3);
  });

  it('keeps different seconds distinct and leaves single-session rows simple', () => {
    const entries = [
      { period: period('Lesson 1', '18:15:00', '18:40:00'), classId: null },
      { period: period('Lesson 1', '18:15:01', '18:40:00'), classId: null },
    ];
    expect(groupPeriods(entries)).toHaveLength(2);
    expect(groupPeriods(entries)[0].sharedName).toBeNull();
    expect(groupPeriods([])).toEqual([]);
  });
});
