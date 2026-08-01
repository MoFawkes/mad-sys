import { Announcement, DeviceAudience, isAnnouncementVisible } from '@/src/domain';

const now = new Date('2026-07-27T12:00:00.000Z');
const teacher: DeviceAudience = {
  role: 'Teacher',
  selectedClassIds: new Set(),
  optedHalfDays: new Set(),
};
const student: DeviceAudience = {
  role: 'StudentDevice',
  selectedClassIds: new Set(['class-a']),
  optedHalfDays: new Set(['am']),
};

function announcement(overrides: Partial<Announcement> = {}): Announcement {
  return {
    id: 'announcement-a',
    title: 'Update',
    body: 'Body',
    createdAt: '2026-07-27T09:00:00.000Z',
    createdBy: 'teacher-a',
    expiresAt: null,
    audienceType: 'everyone',
    audienceClassId: null,
    updateType: 'general',
    publishAt: null,
    eMasjidLink: null,
    status: 'published',
    deletedAt: null,
    isRead: false,
    ...overrides,
  };
}

describe('announcement visibility', () => {
  it('shows a published, due, unexpired, undeleted matching announcement', () => {
    expect(isAnnouncementVisible(announcement(), teacher, now)).toBe(true);
  });

  it.each([
    ['deleted', { deletedAt: '2026-07-27T10:00:00.000Z' }],
    ['draft', { status: 'draft' }],
    ['future', { publishAt: '2026-07-27T12:00:00.001Z' }],
    ['expired before now', { expiresAt: '2026-07-27T11:59:59.999Z' }],
    ['expired exactly now', { expiresAt: '2026-07-27T12:00:00.000Z' }],
  ])('hides an announcement that is %s', (_name, overrides) => {
    expect(isAnnouncementVisible(announcement(overrides), teacher, now)).toBe(false);
  });

  it('includes publish_at exactly now', () => {
    expect(
      isAnnouncementVisible(
        announcement({ publishAt: '2026-07-27T12:00:00.000Z' }),
        teacher,
        now,
      ),
    ).toBe(true);
  });

  it('excludes expires_at exactly now', () => {
    expect(
      isAnnouncementVisible(
        announcement({ expiresAt: '2026-07-27T12:00:00.000Z' }),
        teacher,
        now,
      ),
    ).toBe(false);
  });

  it('applies the audience predicate after publication visibility', () => {
    expect(isAnnouncementVisible(announcement({ audienceType: 'teachers' }), teacher, now)).toBe(true);
    expect(isAnnouncementVisible(announcement({ audienceType: 'teachers' }), student, now)).toBe(false);
    expect(isAnnouncementVisible(announcement({ audienceType: 'am' }), student, now)).toBe(true);
    expect(isAnnouncementVisible(announcement({ audienceType: 'pm' }), student, now)).toBe(false);
    expect(
      isAnnouncementVisible(
        announcement({ audienceType: 'specific_class', audienceClassId: 'class-a' }),
        student,
        now,
      ),
    ).toBe(true);
  });
});
