import Constants from 'expo-constants';
import { Redirect, router } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import {
  Alert,
  Pressable,
  ScrollView,
  Share,
  StyleSheet,
  Switch,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  DEFAULT_NOTIFICATION_SETTINGS,
  getNotificationSettings,
  NotificationSettings,
  saveNotificationSettings,
} from '@/src/notifications';
import { getSupabaseClient } from '@/src/data/sessionStore';
import { exportNotificationDiagnostics } from '@/src/notifications/diagnostics';
import { useApp } from '@/src/ui/AppProvider';
import { theme } from '@/src/ui/theme';

export default function SettingsScreen() {
  const { session, signOut, sync, syncNow } = useApp();
  const [settings, setSettings] = useState<NotificationSettings>(
    DEFAULT_NOTIFICATION_SETTINGS,
  );
  const [syncing, setSyncing] = useState(false);
  const [studentJoinCode, setStudentJoinCode] = useState<string | null>(null);
  const [joinCodeError, setJoinCodeError] = useState<string | null>(null);
  const [exportingDiagnostics, setExportingDiagnostics] = useState(false);

  const canDistributeStudentCode =
    session.status === 'signedIn' &&
    session.mode === 'teacher' &&
    session.role === 'admin' &&
    session.roleVerified;
  const canExportDiagnostics =
    session.status === 'signedIn' &&
    session.mode === 'teacher' &&
    session.role === 'admin' &&
    session.roleVerified;

  useEffect(() => {
    void getNotificationSettings().then(setSettings);
  }, []);

  useEffect(() => {
    if (!canDistributeStudentCode || studentJoinCode) return;
    void getSupabaseClient()
      .rpc('admin_student_join_code')
      .then(({ data, error }) => {
        if (error) setJoinCodeError(error.message);
        else if (typeof data === 'string') setStudentJoinCode(data);
      });
  }, [canDistributeStudentCode, studentJoinCode]);

  const updateSettings = useCallback(
    async (patch: Partial<NotificationSettings>) => {
      const next = await saveNotificationSettings({ ...settings, ...patch });
      setSettings(next);
    },
    [settings],
  );

  if (session.status !== 'signedIn') return <Redirect href="/role-choice" />;

  const confirmSignOut = () => {
    const student = session.mode === 'student';
    Alert.alert(
      student ? 'End student session?' : 'Sign out?',
      student
        ? 'This removes your class choices and cached timetable from this phone.'
        : 'This removes your session and cached timetable from this phone.',
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: student ? 'End session' : 'Sign out',
          style: 'destructive',
          onPress: () => {
            void signOut()
              .then(() => router.replace('/role-choice'))
              .catch((error: unknown) => {
                Alert.alert(
                  'Session ended with cleanup errors',
                  error instanceof Error ? error.message : 'Some local cleanup could not be completed.',
                );
              });
          },
        },
      ],
    );
  };

  const runSync = async () => {
    setSyncing(true);
    try {
      await syncNow();
    } catch (error) {
      Alert.alert('Sync failed', error instanceof Error ? error.message : 'Try again.');
    } finally {
      setSyncing(false);
    }
  };

  const exportDiagnostics = async () => {
    setExportingDiagnostics(true);
    try {
      await exportNotificationDiagnostics();
    } catch (error) {
      Alert.alert(
        'Export failed',
        error instanceof Error ? error.message : 'Could not export diagnostics.',
      );
    } finally {
      setExportingDiagnostics(false);
    }
  };

  return (
    <SafeAreaView edges={['top']} style={styles.screen}>
      <ScrollView contentContainerStyle={styles.content}>
      <Text style={styles.title}>Settings</Text>

      <Section title="Notifications">
        <ToggleRow
          label="Lesson starts"
          onValueChange={(value) => void updateSettings({ lessonStartEnabled: value })}
          value={settings.lessonStartEnabled}
        />
        <ToggleRow
          label="Lesson end warnings"
          onValueChange={(value) => void updateSettings({ endWarningEnabled: value })}
          value={settings.endWarningEnabled}
        />
        <View style={styles.row}>
          <View style={styles.rowCopy}>
            <Text style={styles.rowLabel}>Warning time</Text>
            <Text style={styles.rowDetail}>
              {settings.endWarningMinutes === 0
                ? 'Disabled'
                : `${settings.endWarningMinutes} min before`}
            </Text>
          </View>
          <View style={styles.stepper}>
            <StepButton
              disabled={settings.endWarningMinutes === 0}
              label="−"
              onPress={() =>
                void updateSettings({
                  endWarningMinutes: settings.endWarningMinutes - 1,
                })
              }
            />
            <Text style={styles.stepValue}>{settings.endWarningMinutes}</Text>
            <StepButton
              disabled={settings.endWarningMinutes === 15}
              label="+"
              onPress={() =>
                void updateSettings({
                  endWarningMinutes: settings.endWarningMinutes + 1,
                })
              }
            />
          </View>
        </View>
        <ToggleRow
          label="Announcements"
          onValueChange={(value) => void updateSettings({ announcementsEnabled: value })}
          value={settings.announcementsEnabled}
        />
      </Section>

      {session.mode === 'student' && (
        <Section title="My classes">
          <ActionRow
            detail="Update classes and Naseehah choices"
            label="Change my classes"
            onPress={() => router.push('/student-setup')}
          />
        </Section>
      )}

      {canDistributeStudentCode && (
        <Section title="Student devices">
          <View style={styles.row}>
            <View style={styles.rowCopy}>
              <Text style={styles.rowLabel}>Student join code</Text>
              <Text style={styles.code}>
                {studentJoinCode ? formatJoinCode(studentJoinCode) : 'Loading…'}
              </Text>
              {joinCodeError && <Text style={styles.error}>{joinCodeError}</Text>}
            </View>
          </View>
          <ActionRow
            disabled={!studentJoinCode}
            detail="Share the code and app link"
            label="Share"
            onPress={() => {
              if (!studentJoinCode) return;
              void Share.share({
                message: `Join AQI Clock with code ${formatJoinCode(studentJoinCode)}\naqiclock-mobile://student-setup?code=${studentJoinCode}`,
              });
            }}
          />
        </Section>
      )}

      <Section title="Sync">
        <ActionRow
          detail={
            syncing || sync.connectivity === 'syncing'
              ? 'Syncing…'
              : `Last synced ${formatLastSync(sync.lastSyncedAt)}`
          }
          disabled={syncing || sync.connectivity === 'syncing'}
          label="Sync now"
          onPress={() => void runSync()}
        />
        {sync.error && <Text style={styles.error}>{sync.error}</Text>}
      </Section>

      {canExportDiagnostics && (
        <Section title="Diagnostics">
          <ActionRow
            detail="Share notification deliveries and schedule snapshots as JSON"
            disabled={exportingDiagnostics}
            label={exportingDiagnostics ? 'Preparing export…' : 'Export notification diagnostics'}
            onPress={() => void exportDiagnostics()}
          />
        </Section>
      )}

      <Section title="Account">
        <View style={styles.row}>
          <View style={styles.rowCopy}>
            <Text style={styles.rowLabel}>
              {session.mode === 'student' ? 'Student device' : session.email}
            </Text>
            <Text style={styles.rowDetail}>
              {session.mode === 'student'
                ? 'Personal class selection'
                : session.roleVerified
                  ? session.isActive
                    ? capitalize(session.role)
                    : 'Inactive account'
                  : 'Verifying account…'}
            </Text>
          </View>
        </View>
        <ActionRow
          destructive
          label={session.mode === 'student' ? 'End student session' : 'Sign out'}
          onPress={confirmSignOut}
        />
      </Section>

      <Section title="About">
        <View style={styles.row}>
          <View style={styles.rowCopy}>
            <Text style={styles.rowLabel}>AQI Clock</Text>
            <Text style={styles.rowDetail}>
              Version {Constants.expoConfig?.version ?? '0.11.0'}
            </Text>
          </View>
        </View>
      </Section>
      </ScrollView>
    </SafeAreaView>
  );
}

function Section({ children, title }: React.PropsWithChildren<{ title: string }>) {
  return (
    <View style={styles.section}>
      <Text style={styles.sectionTitle}>{title}</Text>
      <View style={styles.card}>{children}</View>
    </View>
  );
}

function ToggleRow({
  label,
  onValueChange,
  value,
}: {
  label: string;
  onValueChange(value: boolean): void;
  value: boolean;
}) {
  return (
    <View style={styles.row}>
      <Text style={styles.rowLabel}>{label}</Text>
      <Switch
        accessibilityLabel={label}
        onValueChange={onValueChange}
        thumbColor={theme.colors.white}
        trackColor={{ false: theme.colors.fieldBorder, true: theme.colors.blue }}
        value={value}
      />
    </View>
  );
}

function ActionRow({
  destructive = false,
  detail,
  disabled = false,
  label,
  onPress,
}: {
  destructive?: boolean;
  detail?: string;
  disabled?: boolean;
  label: string;
  onPress(): void;
}) {
  return (
    <Pressable
      accessibilityRole="button"
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.row,
        pressed && styles.pressed,
        disabled && styles.disabled,
      ]}>
      <View style={styles.rowCopy}>
        <Text style={[styles.rowLabel, destructive && styles.destructive]}>{label}</Text>
        {detail && <Text style={styles.rowDetail}>{detail}</Text>}
      </View>
      <Text style={[styles.chevron, destructive && styles.destructive]}>›</Text>
    </Pressable>
  );
}

function StepButton({
  disabled,
  label,
  onPress,
}: {
  disabled: boolean;
  label: string;
  onPress(): void;
}) {
  return (
    <Pressable
      accessibilityLabel={label === '+' ? 'Increase warning time' : 'Decrease warning time'}
      accessibilityRole="button"
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.stepButton,
        pressed && styles.pressed,
        disabled && styles.disabled,
      ]}>
      <Text style={styles.stepButtonText}>{label}</Text>
    </Pressable>
  );
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

function capitalize(value: string) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function formatJoinCode(code: string) {
  return code.match(/.{1,4}/g)?.join(' ') ?? code;
}

const styles = StyleSheet.create({
  card: {
    borderColor: theme.colors.panelBorderOnNavy,
    borderRadius: 14,
    borderWidth: 1,
    overflow: 'hidden',
  },
  chevron: { color: theme.colors.mutedTextOnNavy, fontSize: 26 },
  content: {
    backgroundColor: theme.colors.navy,
    flexGrow: 1,
    gap: theme.spacing.lg,
    padding: theme.spacing.md,
    paddingBottom: 40,
  },
  code: {
    color: theme.colors.cream,
    fontFamily: 'monospace',
    fontSize: 20,
    fontWeight: '700',
    letterSpacing: 1,
  },
  destructive: { color: theme.colors.error },
  disabled: { opacity: 0.45 },
  error: {
    color: theme.colors.error,
    fontSize: 14,
    paddingBottom: theme.spacing.md,
    paddingHorizontal: theme.spacing.md,
  },
  pressed: { opacity: 0.65 },
  row: {
    alignItems: 'center',
    borderBottomColor: theme.colors.dividerOnNavy,
    borderBottomWidth: StyleSheet.hairlineWidth,
    flexDirection: 'row',
    justifyContent: 'space-between',
    minHeight: 62,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: 10,
  },
  rowCopy: { flex: 1, gap: 3 },
  rowDetail: { color: theme.colors.mutedTextOnNavy, fontSize: 14 },
  rowLabel: { color: theme.colors.cream, fontSize: 16, fontWeight: '600' },
  section: { gap: theme.spacing.sm },
  sectionTitle: {
    color: theme.colors.mutedTextOnNavy,
    fontSize: 13,
    fontWeight: '800',
    letterSpacing: 0.8,
    marginLeft: 4,
    textTransform: 'uppercase',
  },
  stepButton: {
    alignItems: 'center',
    backgroundColor: theme.colors.subtleSurfaceOnNavy,
    borderRadius: 8,
    height: 36,
    justifyContent: 'center',
    width: 36,
  },
  stepButtonText: { color: theme.colors.blue, fontSize: 24, fontWeight: '600' },
  stepValue: {
    color: theme.colors.cream,
    fontSize: 17,
    fontVariant: ['tabular-nums'],
    fontWeight: '700',
    minWidth: 24,
    textAlign: 'center',
  },
  stepper: { alignItems: 'center', flexDirection: 'row', gap: 10 },
  screen: { backgroundColor: theme.colors.navy, flex: 1 },
  title: { color: theme.colors.cream, fontSize: 30, fontWeight: '800', textAlign: 'center' },
});
