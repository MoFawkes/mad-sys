import { runSignOutTeardown } from '@/src/ui/signOutTeardown';

describe('sign-out teardown', () => {
  it.each(['sync stop', 'cache wipe'])(
    'continues through signed-out state when %s rejects',
    async (failedStep) => {
      const calls: string[] = [];
      const step = (name: string) => async () => {
        calls.push(name);
        if (name === failedStep) throw new Error(`${name} failed`);
      };

      await expect(
        runSignOutTeardown([
          step('remote sign-out'),
          step('sync stop'),
          step('notifications'),
          step('cache wipe'),
          step('clear snapshot'),
          step('clear preferences'),
          step('signed-out state'),
        ]),
      ).rejects.toThrow(`${failedStep} failed`);

      expect(calls).toEqual([
        'remote sign-out',
        'sync stop',
        'notifications',
        'cache wipe',
        'clear snapshot',
        'clear preferences',
        'signed-out state',
      ]);
    },
  );
});
