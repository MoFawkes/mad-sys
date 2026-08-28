import { CACHE_MIGRATIONS } from '@/src/data/migrations';

describe('student preference cache migration', () => {
  it('appends version 2 without changing version 1', () => {
    expect(CACHE_MIGRATIONS.map((migration) => migration.version)).toEqual([1, 2, 3, 4, 5, 6]);
    expect(CACHE_MIGRATIONS[1].sql).toContain('CREATE TABLE student_preferences');
  });
});
