import { Redirect } from 'expo-router';
import { useEffect, useMemo, useState } from 'react';
import { RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { getStatus, Period, resolveDay } from '@/src/domain';
import { useApp } from '@/src/ui/AppProvider';
import { getClasses, getOrganization } from '@/src/data/repositories';
import { deviceZoneDiffers, toInstituteWallClock } from '@/src/time/instituteTime';
import { theme } from '@/src/ui/theme';

const STALE_AFTER_MS = 7 * 24 * 60 * 60 * 1000;

export default function ClockScreen() {
  const { session, snapshot, sync, syncNow } = useApp();
  const [now, setNow] = useState(() => new Date());
  const [instituteTimeZone, setInstituteTimeZone] = useState<string>();
  const [classNames, setClassNames] = useState<ReadonlyMap<string, string>>(new Map());

  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 1000);
    return () => clearInterval(timer);
  }, []);

  useEffect(() => { void getOrganization().then((organization) => setInstituteTimeZone(organization?.timeZone)); }, [sync.lastSyncedAt]);
  useEffect(() => { void getClasses().then((items) => setClassNames(new Map(items.map((item) => [item.id, item.name])))); }, [sync.lastSyncedAt]);
  const instituteNow = useMemo(() => toInstituteWallClock(now, instituteTimeZone), [now, instituteTimeZone]);

  const status = useMemo(() => getStatus(snapshot, instituteNow), [instituteNow, snapshot]);
  const day = useMemo(() => resolveDay(snapshot, instituteNow), [instituteNow, snapshot]);

  if (session.status !== 'signedIn') return <Redirect href="/role-choice" />;
  if (session.mode === 'student' && !session.selectionComplete) {
    return <Redirect href="/student-setup" />;
  }
  if (session.mode === 'teacher' && session.roleVerified && !session.isActive) {
    return (
      <View style={styles.inactive}>
        <Text style={styles.inactiveTitle}>Your account is inactive</Text>
        <Text style={styles.inactiveBody}>
          Contact an administrator to restore access to the timetable.
        </Text>
      </View>
    );
  }

  const isStale =
    sync.lastSyncedAt != null &&
    now.getTime() - sync.lastSyncedAt.getTime() > STALE_AFTER_MS;

  return (
    <SafeAreaView edges={['top']} style={styles.screen}>
      <ScrollView
        contentContainerStyle={styles.content}
        refreshControl={
          <RefreshControl
            onRefresh={syncNow}
            refreshing={sync.connectivity === 'syncing'}
            tintColor={theme.colors.cream}
          />
        }>
        {sync.connectivity === 'offline' && (
          <View style={[styles.banner, isStale && styles.staleBanner]}>
            <Text style={styles.bannerText}>
              {isStale
                ? 'Timetable may be out of date'
                : `Offline — last synced ${formatLastSync(sync.lastSyncedAt)}`}
            </Text>
          </View>
        )}

        <Text style={styles.clock}>{formatClock(instituteNow)}</Text>
        <Text style={styles.date}>{formatDate(instituteNow)}</Text>
        {deviceZoneDiffers(instituteTimeZone) && <Text style={styles.timeZoneNote}>Institute time ({instituteTimeZone}) · your time {formatClock(now).slice(0, 5)}</Text>}

        <View style={styles.hero}>
          <View style={styles.heroHeading}>
            <Text style={styles.eyebrow}>NOW</Text>
            {status.current && (
              <Text style={styles.remainingPill}>
                {formatMinutesRemaining(status.timeRemainingMs ?? 0)}
              </Text>
            )}
          </View>
          <Text style={styles.current}>{status.current?.period.name ?? 'No lesson now'}</Text>
          {status.current && (
            <>
              <Text style={styles.endsAt}>
                ends {status.current.period.endTime.slice(0, 5)}
              </Text>
              <View style={styles.progressTrack}>
                <View
                  style={[
                    styles.progressFill,
                    { width: `${(status.progress ?? 0) * 100}%` },
                  ]}
                />
              </View>
            </>
          )}
          <Text style={styles.next}>
            {status.next
              ? `Next: ${status.next.period.name} at ${status.next.period.startTime.slice(0, 5)}`
              : 'Nothing scheduled'}
          </Text>
        </View>

        <Text style={styles.sectionTitle}>Today</Text>
        {day.periods.length === 0 ? (
          <Text style={styles.empty}>No periods scheduled.</Text>
        ) : (
          day.scheduledPeriods.map(({ period, classId }) => {
            const active = status.current?.period.id === period.id;
            const past = period.endTime.slice(0, 5) <= formatTimeKey(instituteNow) && !active;
            return (
              <View
                key={period.id}
                style={[
                  styles.periodRow,
                  active && styles.activePeriod,
                  past && styles.pastPeriod,
                ]}>
                <View style={styles.periodLabel}>
                  <Text style={styles.periodName}>{period.name}</Text>
                  {snapshot.viewerClassIds && snapshot.viewerClassIds.size > 1 && classId && (
                    <Text style={styles.periodClass}>{classNames.get(classId) ?? 'Selected class'}</Text>
                  )}
                </View>
                <Text style={styles.periodTime}>{formatPeriodTime(period)}</Text>
              </View>
            );
          })
        )}
      </ScrollView>

      <View style={styles.statusStrip}>
        <View
          style={[styles.statusDot, sync.connectivity === 'offline' && styles.offlineDot]}
        />
        <Text style={styles.statusText}>
          {sync.connectivity === 'syncing'
            ? 'Syncing'
            : sync.connectivity === 'offline'
              ? 'Offline'
              : 'Synced'}{' '}
          · {formatLastSync(sync.lastSyncedAt)}
        </Text>
      </View>
    </SafeAreaView>
  );
}

function formatClock(date: Date) {
  return new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).format(date);
}

function formatDate(date: Date) {
  return new Intl.DateTimeFormat(undefined, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  }).format(date);
}

function formatPeriodTime(period: Period) {
  return `${period.startTime.slice(0, 5)}–${period.endTime.slice(0, 5)}`;
}

function formatMinutesRemaining(milliseconds: number) {
  return `${Math.max(1, Math.ceil(milliseconds / 60_000))} min left`;
}

function formatTimeKey(date: Date) {
  return `${date.getHours().toString().padStart(2, '0')}:${date
    .getMinutes()
    .toString()
    .padStart(2, '0')}`;
}

function formatLastSync(date: Date | null) {
  if (!date) return 'never';
  return new Intl.DateTimeFormat(undefined, {
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date);
}

const styles = StyleSheet.create({
  activePeriod: {
    borderColor: theme.colors.cream,
    borderWidth: 1,
    borderLeftWidth: 5,
  },
  banner: { backgroundColor: theme.colors.warningBackground, borderRadius: 8, padding: 10 },
  bannerText: { color: theme.colors.warningText, fontWeight: '600', textAlign: 'center' },
  clock: {
    color: theme.colors.cream,
    fontSize: 58,
    fontVariant: ['tabular-nums'],
    fontWeight: '800',
    textAlign: 'center',
  },
  content: {
    flexGrow: 1,
    gap: theme.spacing.md,
    padding: theme.spacing.lg,
    paddingBottom: 104,
  },
  current: { color: theme.colors.cream, fontSize: 32, fontWeight: '800', marginTop: 24 },
  date: { color: theme.colors.textOnNavy, fontSize: 19, textAlign: 'center' },
  empty: { color: theme.colors.textOnNavy, textAlign: 'center' },
  endsAt: { color: theme.colors.textOnNavy, fontSize: 18, marginTop: 4 },
  eyebrow: { color: theme.colors.textOnNavy, fontSize: 14, fontWeight: '800', letterSpacing: 1.8 },
  hero: {
    borderColor: theme.colors.strongBorderOnNavy,
    borderLeftColor: theme.colors.cream,
    borderLeftWidth: 5,
    borderRadius: 16,
    borderWidth: 1,
    marginTop: theme.spacing.lg,
    padding: theme.spacing.lg,
  },
  heroHeading: { alignItems: 'center', flexDirection: 'row', justifyContent: 'space-between' },
  inactive: {
    alignItems: 'center',
    backgroundColor: theme.colors.navy,
    flex: 1,
    gap: theme.spacing.sm,
    justifyContent: 'center',
    padding: theme.spacing.lg,
  },
  inactiveBody: { color: theme.colors.textOnNavy, fontSize: 17, lineHeight: 24, textAlign: 'center' },
  inactiveTitle: {
    color: theme.colors.cream,
    fontSize: 26,
    fontWeight: '700',
    textAlign: 'center',
  },
  next: { color: theme.colors.textOnNavy, fontSize: 17, fontWeight: '600', marginTop: theme.spacing.lg },
  offlineDot: { backgroundColor: theme.colors.warning },
  pastPeriod: { opacity: 0.4 },
  periodName: { color: theme.colors.cream, flex: 1, fontSize: 17, fontWeight: '600' },
  periodLabel: { flex: 1 },
  periodClass: { color: theme.colors.textOnNavy, fontSize: 13, marginTop: 2 },
  periodRow: {
    alignItems: 'center',
    borderColor: 'transparent',
    borderRadius: 10,
    flexDirection: 'row',
    gap: theme.spacing.md,
    justifyContent: 'space-between',
    minHeight: 58,
    padding: theme.spacing.md,
  },
  periodTime: {
    color: theme.colors.cream,
    fontVariant: ['tabular-nums'],
    fontWeight: '700',
    textAlign: 'right',
    width: 112,
  },
  progressFill: { backgroundColor: theme.colors.cream, borderRadius: 4, height: 8 },
  progressTrack: {
    backgroundColor: theme.colors.trackOnNavy,
    borderRadius: 4,
    height: 8,
    marginTop: theme.spacing.lg,
    overflow: 'hidden',
  },
  remainingPill: {
    backgroundColor: theme.colors.raisedSurfaceOnNavy,
    borderRadius: 999,
    color: theme.colors.cream,
    fontSize: 16,
    fontWeight: '700',
    overflow: 'hidden',
    paddingHorizontal: 14,
    paddingVertical: 8,
  },
  screen: { backgroundColor: theme.colors.navy, flex: 1 },
  sectionTitle: {
    color: theme.colors.faintTextOnNavy,
    fontSize: 18,
    fontWeight: '800',
    letterSpacing: 1.5,
    marginTop: theme.spacing.lg,
    textTransform: 'uppercase',
  },
  staleBanner: { backgroundColor: theme.colors.staleBackground },
  statusDot: { backgroundColor: theme.colors.success, borderRadius: 999, height: 10, width: 10 },
  statusStrip: {
    alignItems: 'center',
    backgroundColor: theme.colors.navy,
    borderColor: theme.colors.trackOnNavy,
    borderWidth: 1,
    bottom: 0,
    flexDirection: 'row',
    gap: 10,
    left: 0,
    minHeight: 54,
    paddingHorizontal: theme.spacing.lg,
    position: 'absolute',
    right: 0,
  },
  statusText: { color: theme.colors.textOnNavy, fontSize: 15, fontWeight: '600' },
  timeZoneNote: { color: theme.colors.textOnNavy, fontSize: 14, textAlign: 'center' },
});
