import NetInfo from '@react-native-community/netinfo';
import { Session } from '@supabase/supabase-js';
import * as Linking from 'expo-linking';
import { router } from 'expo-router';
import {
  createContext,
  PropsWithChildren,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { AppState } from 'react-native';

import {
  getCachedProfile,
  getOrganization,
  getStudentPreferences,
  loadScheduleSnapshot,
  saveStudentPreferences,
  StudentPreferences,
} from '@/src/data/repositories';
import { getSupabaseClient } from '@/src/data/sessionStore';
import { getMeta, setMeta, wipeCache } from '@/src/data/sqlite';
import {
  STUDENT_DEVICE_REVOKED_MESSAGE,
  SyncState,
  syncService,
} from '@/src/data/syncService';
import {
  DeviceAudience,
  EMPTY_SNAPSHOT,
  filterScheduleForAudience,
  ScheduleSnapshot,
} from '@/src/domain';
import {
  cancelAllScheduledAqiClockNotifications,
  cancelScheduledLessonNotifications,
  getNotificationSettings,
  initializeNotificationsAsync,
  processAnnouncementNotifications,
  reconcileScheduledNotifications,
  reconcileScheduledNotificationsFromCache,
  registerNotificationBackgroundTaskAsync,
  subscribeNotificationSettings,
} from '@/src/notifications';
import { runSignOutTeardown } from '@/src/ui/signOutTeardown';

type Role = 'teacher' | 'admin' | 'graduate';
type SessionState =
  | { status: 'loading' | 'signedOut' | 'reauthenticationRequired' }
  | {
      status: 'signedIn';
      userId: string;
      email: string;
      role: Role | 'student';
      mode: 'teacher' | 'student';
      roleVerified: boolean;
      isActive: boolean;
      selectionComplete: boolean;
      studentEnrolled: boolean;
    };

type AppContextValue = {
  session: SessionState;
  sync: SyncState;
  snapshot: ScheduleSnapshot;
  audience: DeviceAudience;
  dataRevision: number;
  signIn(email: string, password: string): Promise<void>;
  enrollStudent(joinCode: string): Promise<void>;
  saveStudentSelection(preferences: StudentPreferences): Promise<void>;
  signOut(): Promise<void>;
  syncNow(): Promise<void>;
};

const AppContext = createContext<AppContextValue | null>(null);

export function AppProvider({ children }: PropsWithChildren) {
  const [session, setSession] = useState<SessionState>({ status: 'loading' });
  const [sync, setSync] = useState<SyncState>({
    connectivity: 'offline',
    lastSyncedAt: null,
    error: null,
  });
  const [snapshot, setSnapshot] = useState<ScheduleSnapshot>(EMPTY_SNAPSHOT);
  const [studentPreferences, setStudentPreferences] = useState<StudentPreferences | null>(null);
  const [dataRevision, setDataRevision] = useState(0);
  const notificationPermissionGranted = useRef<boolean | null>(null);

  const audience = useMemo(
    () => buildAudience(session, studentPreferences),
    [session, studentPreferences],
  );
  const visibleSnapshot = useMemo(
    () => filterScheduleForAudience(snapshot, audience),
    [audience, snapshot],
  );

  const reloadSnapshot = useCallback(async () => {
    setSnapshot(await loadScheduleSnapshot());
  }, []);

  const applyFreshProfile = useCallback(async (userId: string) => {
    const profile = await getCachedProfile(userId);
    setSession((current) =>
      // Teacher sessions only. An anonymous student never has a profile row
      // (ADR-023), so the "no own row" signal below would misread it as an
      // inactive account and flip the session into teacher mode.
      current.status === 'signedIn' && current.userId === userId && current.mode === 'teacher'
        ? {
            ...current,
            role: profile?.role ?? 'teacher',
            mode: 'teacher',
            roleVerified: true,
            // A successful profiles snapshot with no own row is how RLS exposes
            // an inactive (or otherwise unprovisioned) invited account.
            isActive: profile?.isActive ?? false,
            selectionComplete: true,
            studentEnrolled: false,
          }
        : current,
    );
  }, []);

  const beginTeacherSession = useCallback(
    async (authSession: Session) => {
      const userId = authSession.user.id;
      const cached = await getCachedProfile(userId);
      // A restored cached admin is intentionally downgraded until profiles sync succeeds.
      const initialRole = cached?.role === 'admin' ? 'teacher' : (cached?.role ?? 'teacher');
      setSession({
        status: 'signedIn',
        userId,
        email: authSession.user.email ?? '',
        role: initialRole,
        mode: 'teacher',
        roleVerified: false,
        isActive: cached?.isActive ?? false,
        selectionComplete: true,
        studentEnrolled: false,
      });
      setStudentPreferences(null);
      await reloadSnapshot();
      await syncService.start(userId, 'teacher');
    },
    [reloadSnapshot],
  );

  const beginStudentSession = useCallback(
    async (authSession: Session) => {
      const preferences = await getStudentPreferences();
      const enrolled = (await getMeta('student_enrolled')) === 'true';
      setStudentPreferences(preferences);
      setSession({
        status: 'signedIn',
        userId: authSession.user.id,
        email: '',
        role: 'student',
        mode: 'student',
        roleVerified: true,
        isActive: true,
        selectionComplete: (preferences?.selectedClassIds.length ?? 0) > 0,
        studentEnrolled: enrolled,
      });
      await reloadSnapshot();
      await syncService.start(authSession.user.id, 'student');
    },
    [reloadSnapshot],
  );

  useEffect(() => {
    const unsubscribeSync = syncService.subscribe((next, changedTable) => {
      setSync(next);
      if (next.error === STUDENT_DEVICE_REVOKED_MESSAGE) {
        // SyncService emits this only after its transactional wipeCache() has
        // removed the cached snapshots, preferences, and enrollment metadata.
        void syncService.stop().catch(() => {
          // Re-enrolment can still restart sync on the next foreground attempt.
        });
        setSnapshot(EMPTY_SNAPSHOT);
        setStudentPreferences(null);
        setSession((current) =>
          current.status === 'signedIn' && current.mode === 'student'
            ? { ...current, studentEnrolled: false, selectionComplete: false }
            : current,
        );
        router.replace({
          pathname: '/student-setup',
          params: { message: STUDENT_DEVICE_REVOKED_MESSAGE },
        });
      }
      if (changedTable) setDataRevision((value) => value + 1);
      if (changedTable === 'profiles') {
        setSession((current) => {
          if (current.status === 'signedIn') void applyFreshProfile(current.userId);
          return current;
        });
      }
      if (
        changedTable &&
        [
          'timetables',
          'periods',
          'period_classes',
          'week_schedule',
          'date_overrides',
        ].includes(changedTable)
      ) {
        void reloadSnapshot();
      }
    });

    const supabase = getSupabaseClient();
    void supabase.auth.getSession().then(async ({ data, error }) => {
      if (error) {
        setSession({ status: 'reauthenticationRequired' });
      } else if (data.session) {
        if (data.session.user.is_anonymous) {
          await beginStudentSession(data.session);
        } else {
          await beginTeacherSession(data.session);
        }
      } else {
        setSession({ status: 'signedOut' });
      }
    });

    const auth = supabase.auth.onAuthStateChange((event) => {
      if (event === 'SIGNED_OUT') setSession({ status: 'signedOut' });
    });
    const appState = AppState.addEventListener('change', (state) => {
      if (state === 'active') {
        supabase.auth.startAutoRefresh();
        void syncService.syncAll();
        void reconcileScheduledNotificationsFromCache().catch(() => {
          // Background reconciliation is best-effort.
        });
      } else {
        supabase.auth.stopAutoRefresh();
      }
    });
    const network = NetInfo.addEventListener((state) => {
      if (state.isConnected) void syncService.syncAll();
    });
    const linking = Linking.addEventListener('url', ({ url }) => {
      void acceptRecoveryUrl(url);
    });
    void Linking.getInitialURL().then((url) => {
      if (url) void acceptRecoveryUrl(url);
    });

    return () => {
      unsubscribeSync();
      auth.data.subscription.unsubscribe();
      appState.remove();
      network();
      linking.remove();
    };
  }, [applyFreshProfile, beginStudentSession, beginTeacherSession, reloadSnapshot]);

  useEffect(
    () => subscribeNotificationSettings(() => setDataRevision((value) => value + 1)),
    [],
  );

  useEffect(() => {
    if (session.status !== 'signedIn') {
      return;
    }
    if (session.mode === 'student' && !session.selectionComplete) {
      void cancelScheduledLessonNotifications().catch(() => {
        // Selection revocation must disarm the previous class plan.
      });
      return;
    }
    if (session.mode === 'teacher' && session.roleVerified && !session.isActive) {
      void cancelScheduledLessonNotifications().catch(() => {
        // Account-state cleanup must not affect the inactive UI.
      });
      return;
    }

    void (async () => {
      const granted =
        notificationPermissionGranted.current ??
        (await initializeNotificationsAsync());
      notificationPermissionGranted.current = granted;
      await registerNotificationBackgroundTaskAsync();
      if (granted) {
        const organization = await getOrganization();
        await reconcileScheduledNotifications(
          visibleSnapshot,
          audience,
          await getNotificationSettings(),
          new Date(),
          organization?.timeZone,
        );
      }
      await processAnnouncementNotifications(audience);
    })().catch(() => {
      // Notification denial or platform scheduling failure must never break the clock.
    });
  }, [audience, dataRevision, session, visibleSnapshot]);

  const value = useMemo<AppContextValue>(
    () => {
      return {
        session,
        sync,
        snapshot: visibleSnapshot,
        audience,
        dataRevision,
        async signIn(email, password) {
          const supabase = getSupabaseClient();
          const result = await supabase.auth.signInWithPassword({
            email: email.trim(),
            password,
          });
          if (result.error) throw result.error;
          await beginTeacherSession(result.data.session);
        },
        async enrollStudent(joinCode) {
          const supabase = getSupabaseClient();
          let { data } = await supabase.auth.getSession();
          if (!data.session?.user.is_anonymous) {
            const result = await supabase.auth.signInAnonymously();
            if (result.error) throw result.error;
            data = { session: result.data.session };
          }
          if (!data.session) throw new Error('Could not start the student session.');

          const enrollment = await supabase.rpc('enroll_student_device', {
            join_code: joinCode.trim(),
          });
          if (enrollment.error) throw enrollment.error;
          await setMeta('student_enrolled', 'true');
          await beginStudentSession(data.session);
        },
        async saveStudentSelection(preferences) {
          await saveStudentPreferences(preferences);
          setStudentPreferences(preferences);
          setSession((current) =>
            current.status === 'signedIn' && current.mode === 'student'
              ? { ...current, selectionComplete: true }
              : current,
          );
          setDataRevision((revision) => revision + 1);
        },
        async signOut() {
          await runSignOutTeardown([
            async () => {
              const result = await getSupabaseClient().auth.signOut();
              if (result.error) throw result.error;
            },
            () => syncService.stop(),
            cancelAllScheduledAqiClockNotifications,
            wipeCache,
            async () => setSnapshot(EMPTY_SNAPSHOT),
            async () => setStudentPreferences(null),
            async () => setSession({ status: 'signedOut' }),
          ]);
        },
        async syncNow() {
          await syncService.syncAll();
        },
      };
    },
    [
      beginStudentSession,
      beginTeacherSession,
      audience,
      dataRevision,
      session,
      visibleSnapshot,
      sync,
    ],
  );

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>;
}

function buildAudience(
  session: SessionState,
  preferences: StudentPreferences | null,
): DeviceAudience {
  return {
    role:
      session.status === 'signedIn' && session.mode === 'student'
        ? 'StudentDevice'
        : session.status === 'signedIn' && session.role === 'admin'
          ? 'Admin'
          : 'Teacher',
    selectedClassIds: new Set(preferences?.selectedClassIds ?? []),
    optedHalfDays: new Set([
      ...(preferences?.optedAm ? (['am'] as const) : []),
      ...(preferences?.optedPm ? (['pm'] as const) : []),
    ]),
  };
}

export function useApp(): AppContextValue {
  const value = useContext(AppContext);
  if (!value) throw new Error('useApp must be used within AppProvider.');
  return value;
}

async function acceptRecoveryUrl(url: string): Promise<void> {
  if (!url.startsWith('aqiclock-mobile://reset-password')) return;
  const parameters = new URLSearchParams(url.replace('#', '?').split('?')[1] ?? '');
  const accessToken = parameters.get('access_token');
  const refreshToken = parameters.get('refresh_token');
  if (accessToken && refreshToken) {
    await getSupabaseClient().auth.setSession({
      access_token: accessToken,
      refresh_token: refreshToken,
    });
  }
}
