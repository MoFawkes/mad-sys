import { useFocusEffect } from '@react-navigation/native';
import * as Linking from 'expo-linking';
import { Redirect } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import {
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  getVisibleAnnouncements,
  markAnnouncementRead,
} from '@/src/data/repositories';
import { Announcement } from '@/src/domain';
import { useApp } from '@/src/ui/AppProvider';
import { theme } from '@/src/ui/theme';

export default function AnnouncementsScreen() {
  const { audience, dataRevision, session, sync, syncNow } = useApp();
  const [items, setItems] = useState<Announcement[]>([]);

  const load = useCallback(async () => {
    setItems(await getVisibleAnnouncements(audience));
  }, [audience]);

  useEffect(() => {
    void load();
  }, [dataRevision, load]);

  useFocusEffect(
    useCallback(() => {
      void load();
    }, [load]),
  );

  if (session.status !== 'signedIn') return <Redirect href="/role-choice" />;
  if (session.mode === 'student' && !session.selectionComplete) {
    return <Redirect href="/student-setup" />;
  }

  async function markRead(item: Announcement) {
    if (!item.isRead) {
      await markAnnouncementRead(item.id);
      await load();
    }
  }

  return (
    <SafeAreaView edges={['top']} style={styles.screen}>
      <ScrollView
        contentContainerStyle={styles.content}
        refreshControl={
          <RefreshControl
            onRefresh={async () => {
              await syncNow();
              await load();
            }}
            refreshing={sync.connectivity === 'syncing'}
            tintColor={theme.colors.cream}
          />
        }>
      <Text style={styles.screenTitle}>Announcements</Text>
      <Text style={styles.subtitle}>Stay updated with the latest notices.</Text>
      {items.length === 0 ? (
        <Text style={styles.empty}>No current announcements.</Text>
      ) : (
        items.map((item) => (
          <Pressable
            key={item.id}
            accessibilityRole="button"
            onPress={() => markRead(item)}
            style={[styles.card, !item.isRead && styles.unreadCard]}>
            <View style={styles.heading}>
              <Text style={styles.badge}>{formatUpdateType(item.updateType)}</Text>
              <View style={styles.timestamp}>
                {!item.isRead && <View style={styles.unreadDot} />}
                <Text style={styles.timestampText}>
                  {formatRelativeTime(item.publishAt ?? item.createdAt)}
                </Text>
              </View>
            </View>
            <Text style={styles.title}>{item.title}</Text>
            <Text numberOfLines={2} style={styles.body}>{item.body}</Text>
            {item.eMasjidLink && (
              <Pressable
                accessibilityRole="link"
                style={styles.linkRow}
                onPress={() => Linking.openURL(item.eMasjidLink!)}>
                <Text style={styles.link}>Open on eMasjid ↗</Text>
              </Pressable>
            )}
          </Pressable>
        ))
      )}
      </ScrollView>
    </SafeAreaView>
  );
}

function formatUpdateType(value: string): string {
  return value
    .split('_')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

function formatRelativeTime(value: string): string {
  const elapsed = Math.max(0, Date.now() - new Date(value).getTime());
  const minutes = Math.floor(elapsed / 60_000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} ${hours === 1 ? 'hour' : 'hours'} ago`;
  const days = Math.floor(hours / 24);
  if (days < 7) return days === 1 ? 'yesterday' : `${days} days ago`;
  const weeks = Math.floor(days / 7);
  return `${weeks} ${weeks === 1 ? 'week' : 'weeks'} ago`;
}

const styles = StyleSheet.create({
  badge: {
    alignSelf: 'flex-start',
    backgroundColor: theme.colors.subtleSurfaceOnNavy,
    borderColor: theme.colors.panelBorderOnNavy,
    borderRadius: 5,
    borderWidth: 1,
    color: theme.colors.linkOnNavy,
    fontSize: 12,
    fontWeight: '700',
    overflow: 'hidden',
    paddingHorizontal: 10,
    paddingVertical: 4,
  },
  body: { color: theme.colors.textOnNavy, fontSize: 16, lineHeight: 23 },
  card: {
    backgroundColor: theme.colors.deepNavy,
    borderColor: theme.colors.trackOnNavy,
    borderRadius: 14,
    borderWidth: 1,
    gap: 10,
    padding: theme.spacing.md,
  },
  content: {
    backgroundColor: theme.colors.navy,
    flexGrow: 1,
    gap: theme.spacing.md,
    padding: theme.spacing.lg,
    paddingBottom: 42,
  },
  empty: { color: theme.colors.textOnNavy, fontSize: 17, marginTop: 80, textAlign: 'center' },
  heading: { alignItems: 'center', flexDirection: 'row', justifyContent: 'space-between' },
  link: { color: theme.colors.linkOnNavy, fontSize: 15, fontWeight: '700' },
  linkRow: { paddingTop: 2 },
  screenTitle: { color: theme.colors.cream, fontSize: 30, fontWeight: '800', marginTop: 18 },
  screen: { backgroundColor: theme.colors.navy, flex: 1 },
  subtitle: { color: theme.colors.textOnNavy, fontSize: 17, marginBottom: theme.spacing.lg },
  timestamp: { alignItems: 'center', flexDirection: 'row', gap: 8 },
  timestampText: { color: theme.colors.textOnNavy, fontSize: 13, fontWeight: '600' },
  title: { color: theme.colors.cream, fontSize: 21, fontWeight: '700' },
  unreadCard: { borderLeftColor: theme.colors.cream, borderLeftWidth: 5 },
  unreadDot: { backgroundColor: theme.colors.unreadOnNavy, borderRadius: 999, height: 8, width: 8 },
});
