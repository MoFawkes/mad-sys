import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Tabs } from 'expo-router';

import { theme } from '@/src/ui/theme';

export default function TabsLayout() {
  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveBackgroundColor: theme.colors.selectedNavy,
        tabBarActiveTintColor: theme.colors.cream,
        tabBarInactiveTintColor: theme.colors.mutedTextOnNavy,
        tabBarLabelStyle: { fontSize: 12, fontWeight: '700' },
        tabBarStyle: {
          backgroundColor: theme.colors.deepNavy,
          borderTopColor: theme.colors.trackOnNavy,
          minHeight: 68,
          paddingBottom: 8,
          paddingTop: 7,
        },
      }}>
      <Tabs.Screen
        name="clock"
        options={{
          title: 'Clock',
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons color={color} name="clock-outline" size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="announcements"
        options={{
          title: 'Announcements',
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons color={color} name="bullhorn-outline" size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="settings"
        options={{
          title: 'Settings',
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons color={color} name="cog-outline" size={size} />
          ),
        }}
      />
    </Tabs>
  );
}
