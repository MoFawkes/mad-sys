import type * as Notifications from 'expo-notifications';

import {
  notificationToDelivery,
  sweepPresentedNotifications,
} from '@/src/notifications/deliveryEvidence';

jest.mock('expo-notifications', () => ({
  addNotificationReceivedListener: jest.fn(),
  getPresentedNotificationsAsync: jest.fn(),
}));
jest.mock('expo-sqlite', () => ({ openDatabaseAsync: jest.fn() }));
jest.mock('@/src/data/repositories', () => ({
  recordNotificationDelivery: jest.fn(async () => undefined),
}));

describe('notification delivery evidence', () => {
  beforeEach(() => jest.clearAllMocks());

  it('maps the native delivery timestamp and AQI metadata', () => {
    const notification = {
      date: Date.parse('2026-08-27T18:40:00.000Z'),
      request: {
        identifier: 'start:lesson:2026-08-27',
        content: {
          title: 'Lesson starting',
          body: 'Lesson 1',
          data: { aqiTriggerTime: Date.parse('2026-08-27T18:40:00.000Z') },
        },
      },
    } as unknown as Notifications.Notification;

    expect(notificationToDelivery(notification, 'foreground_listener')).toEqual({
      eventKey: 'start:lesson:2026-08-27',
      identifier: 'start:lesson:2026-08-27',
      deliveredAt: '2026-08-27T18:40:00.000Z',
      observedVia: 'foreground_listener',
      title: 'Lesson starting',
      body: 'Lesson 1',
      triggerTime: '2026-08-27T18:40:00.000Z',
    });
  });

  it('records tray-resident notifications as presented sweeps', async () => {
    const notification = {
      date: Date.parse('2026-08-27T18:40:00.000Z'),
      request: {
        identifier: 'start:lesson:2026-08-27',
        content: { title: 'Lesson starting', body: 'Lesson 1', data: {} },
      },
    } as unknown as Notifications.Notification;
    const NotificationsModule = jest.requireMock('expo-notifications') as {
      getPresentedNotificationsAsync: jest.Mock;
    };
    const { recordNotificationDelivery } = jest.requireMock(
      '@/src/data/repositories',
    ) as { recordNotificationDelivery: jest.Mock };
    NotificationsModule.getPresentedNotificationsAsync.mockResolvedValue([notification]);

    await sweepPresentedNotifications();

    expect(recordNotificationDelivery).toHaveBeenCalledWith(expect.objectContaining({
      identifier: 'start:lesson:2026-08-27',
      observedVia: 'presented_sweep',
    }));
  });

  it('serializes presented-notification writes', async () => {
    const NotificationsModule = jest.requireMock('expo-notifications') as {
      getPresentedNotificationsAsync: jest.Mock;
    };
    const { recordNotificationDelivery } = jest.requireMock(
      '@/src/data/repositories',
    ) as { recordNotificationDelivery: jest.Mock };
    const notifications = ['one', 'two', 'three'].map((identifier, index) => ({
      date: Date.parse(`2026-08-27T18:4${index}:00.000Z`),
      request: { identifier, content: { data: {} } },
    })) as unknown as Notifications.Notification[];
    let active = 0;
    let maximumActive = 0;
    recordNotificationDelivery.mockImplementation(async () => {
      active += 1;
      maximumActive = Math.max(maximumActive, active);
      await Promise.resolve();
      active -= 1;
    });
    NotificationsModule.getPresentedNotificationsAsync.mockResolvedValue(notifications);

    await sweepPresentedNotifications();

    expect(recordNotificationDelivery).toHaveBeenCalledTimes(3);
    expect(maximumActive).toBe(1);
  });
});
