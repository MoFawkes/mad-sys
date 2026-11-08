import React from 'react';
import { Alert, AppState } from 'react-native';
import { act, create } from 'react-test-renderer';

import { getMeta, setMeta } from '@/src/data/sqlite';
import { fetchAndroidUpdate } from '@/src/updates/releases';
import { getIncludeBetaUpdates } from '@/src/updates/settings';
import { UpdateChecker } from '@/src/updates/UpdateChecker';

jest.mock('expo-constants', () => ({ __esModule: true, default: { expoConfig: { version: '0.16.0-beta.2' } } }));
jest.mock('expo-linking', () => ({ openURL: jest.fn() }));
jest.mock('react-native', () => ({ Alert: { alert: jest.fn() }, Platform: { OS: 'android' }, AppState: { currentState: 'active', addEventListener: jest.fn(() => ({ remove: jest.fn() })) } }));
jest.mock('@/src/data/sqlite', () => ({ getMeta: jest.fn(), setMeta: jest.fn() }));
jest.mock('@/src/updates/releases', () => ({ fetchAndroidUpdate: jest.fn() }));
jest.mock('@/src/updates/settings', () => ({ getIncludeBetaUpdates: jest.fn(), subscribeUpdateSettings: jest.fn(() => jest.fn()) }));

describe('automatic Android update checks', () => {
  let tree: ReturnType<typeof create>;
  beforeEach(() => {
    jest.clearAllMocks();
    jest.mocked(getMeta).mockResolvedValue(null);
    jest.mocked(setMeta).mockResolvedValue();
    jest.mocked(getIncludeBetaUpdates).mockResolvedValue(false);
    jest.mocked(fetchAndroidUpdate).mockResolvedValue({ version: '0.17.0', url: 'https://github.com/test.apk' });
  });
  afterEach(async () => { if (tree) await act(async () => tree.unmount()); });

  it('checks the chosen channel and prompts once without downloading automatically', async () => {
    await act(async () => { tree = create(<UpdateChecker />); });
    expect(fetchAndroidUpdate).toHaveBeenCalledWith('0.16.0-beta.2', false);
    expect(Alert.alert).toHaveBeenCalledWith('AQI Clock update available', expect.any(String), expect.arrayContaining([expect.objectContaining({ text: 'Download' })]));
    expect(setMeta).toHaveBeenCalledWith('updates_last_prompt', '0.17.0');
  });
  it('does not poll again within a day or repeat a dismissed version', async () => {
    jest.mocked(getMeta).mockImplementation(async key => key === 'updates_last_check' ? String(Date.now()) : null);
    await act(async () => { tree = create(<UpdateChecker />); });
    expect(fetchAndroidUpdate).not.toHaveBeenCalled();
    jest.mocked(getMeta).mockImplementation(async key => key === 'updates_last_prompt' ? '0.17.0' : null);
    const foreground = jest.mocked(AppState.addEventListener).mock.calls.at(-1)![1];
    await act(async () => foreground('active'));
    expect(fetchAndroidUpdate).toHaveBeenCalledTimes(1);
    expect(Alert.alert).not.toHaveBeenCalled();
  });
  it('does not prompt with a stale beta response after opting out', async () => {
    jest.mocked(getIncludeBetaUpdates).mockResolvedValueOnce(true).mockResolvedValue(false);
    await act(async () => { tree = create(<UpdateChecker />); });
    expect(Alert.alert).not.toHaveBeenCalled();
  });
  it('handles offline checks quietly and records the attempt to avoid retry storms', async () => {
    jest.mocked(fetchAndroidUpdate).mockRejectedValue(new Error('Offline'));
    await act(async () => { tree = create(<UpdateChecker />); });
    expect(setMeta).toHaveBeenCalledWith('updates_last_check', expect.any(String));
    expect(Alert.alert).not.toHaveBeenCalled();
  });
});
