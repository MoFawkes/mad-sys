import Constants from 'expo-constants';
import * as Linking from 'expo-linking';
import { useEffect } from 'react';
import { Alert, AppState, Platform } from 'react-native';

import { getMeta, setMeta } from '@/src/data/sqlite';
import { fetchAndroidUpdate } from '@/src/updates/releases';
import { getIncludeBetaUpdates, subscribeUpdateSettings } from '@/src/updates/settings';

export function UpdateChecker() {
  useEffect(() => {
    if (Platform.OS !== 'android') return;
    let busy = false;
    let active = true;
    const check = async () => {
      if (busy || AppState.currentState !== 'active') return;
      busy = true;
      try {
        const last = Number(await getMeta('updates_last_check'));
        if (Date.now() - last < 24 * 60 * 60 * 1000) return;
        const beta = await getIncludeBetaUpdates();
        const version = Constants.expoConfig?.version;
        if (!version) return;
        await setMeta('updates_last_check', String(Date.now()));
        const update = await fetchAndroidUpdate(version, beta);
        if (!active || beta !== await getIncludeBetaUpdates()) return;
        if (!update || AppState.currentState !== 'active' || await getMeta('updates_last_prompt') === update.version) return;
        await setMeta('updates_last_prompt', update.version);
        Alert.alert('AQI Clock update available', `Version ${update.version} is ready. Download the APK, then approve installation to update your app.`, [
          { text: 'Later', style: 'cancel' },
          { text: 'Download', onPress: () => { void Linking.openURL(update.url).catch(() => Alert.alert('Could not open download', 'Try Check for updates in Settings.')); } },
        ]);
      } catch {
        // Offline/rate-limited checks are retried later without interrupting lessons.
      } finally {
        busy = false;
      }
    };
    void check();
    const subscription = AppState.addEventListener('change', state => { if (state === 'active') void check(); });
    const unsubscribe = subscribeUpdateSettings(() => { void check(); });
    return () => { active = false; subscription.remove(); unsubscribe(); };
  }, []);
  return null;
}
