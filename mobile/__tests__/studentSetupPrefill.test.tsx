import React from 'react';
import { TextInput } from 'react-native';
import { act, create } from 'react-test-renderer';

import StudentSetupScreen from '@/app/student-setup';

jest.mock('expo-router', () => ({
  Redirect: () => null,
  router: { replace: jest.fn() },
  useLocalSearchParams: () => ({ code: 'ABCD2345EFGH6789' }),
}));

jest.mock('@/src/ui/AppProvider', () => ({
  useApp: () => ({
    dataRevision: 0,
    enrollStudent: jest.fn(),
    saveStudentSelection: jest.fn(),
    session: { status: 'signedOut' },
  }),
}));

jest.mock('@/src/data/repositories', () => ({
  getClasses: jest.fn(async () => []),
  getStudentPreferences: jest.fn(async () => null),
}));

jest.mock('@/src/ui/components', () => ({
  Field: (props: object) =>
    jest.requireActual<typeof import('react')>('react').createElement(
      jest.requireActual<typeof import('react-native')>('react-native').TextInput,
      props,
    ),
  PrimaryButton: ({ children }: { children?: import('react').ReactNode }) => children,
}));

describe('student setup deep link', () => {
  it('prefills the join code from the route without submitting it', async () => {
    let tree: ReturnType<typeof create>;
    await act(async () => {
      tree = create(<StudentSetupScreen />);
    });

    expect(tree!.root.findByType(TextInput).props.value).toBe('ABCD2345EFGH6789');
  });
});
