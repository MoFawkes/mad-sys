import 'react-native-url-polyfill/auto';

import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { useEffect } from 'react';
import 'react-native-reanimated';

import '@/src/notifications/background';
import { registerNotificationDeliveryCapture } from '@/src/notifications/deliveryEvidence';
import { AppProvider } from '@/src/ui/AppProvider';
import { UpdateChecker } from '@/src/updates/UpdateChecker';

export default function RootLayout() {
  useEffect(() => registerNotificationDeliveryCapture(), []);

  return (
    <AppProvider>
      <UpdateChecker />
      <StatusBar style="light" />
      <Stack screenOptions={{ headerShown: false }} />
    </AppProvider>
  );
}
