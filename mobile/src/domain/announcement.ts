import {
  AnnouncementAudience,
  DeviceAudience,
  matchesAnnouncement,
} from './audience';

export type AnnouncementUpdateType =
  | 'general'
  | 'class_starts'
  | 'naseehah'
  | 'monthly_programme'
  | 'yearly_programme';

export type Announcement = {
  id: string;
  title: string;
  body: string;
  createdAt: string;
  createdBy: string;
  expiresAt: string | null;
  audienceType: AnnouncementAudience;
  audienceClassId: string | null;
  updateType: AnnouncementUpdateType;
  publishAt: string | null;
  eMasjidLink: string | null;
  status: string;
  deletedAt: string | null;
  isRead: boolean;
};

export function isAnnouncementVisible(
  item: Announcement,
  audience: DeviceAudience,
  now: Date,
): boolean {
  const instant = now.getTime();
  return (
    item.deletedAt == null &&
    item.status !== 'draft' &&
    (item.publishAt == null || new Date(item.publishAt).getTime() <= instant) &&
    (item.expiresAt == null || new Date(item.expiresAt).getTime() > instant) &&
    matchesAnnouncement(audience, item)
  );
}
