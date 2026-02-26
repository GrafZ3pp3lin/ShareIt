import type { User } from 'oidc-client-ts';
import { createContext, createEffect, createMemo, createSignal, onCleanup, useContext } from 'solid-js';
import { getOidcSettings, getUserManager } from '../../shared/auth/oidc';

type AuthContextValue = {
  isEnabled: boolean;
  isLoading: boolean;
  isAuthenticated: boolean;
  user: User | null;
  login: () => Promise<void> | void;
  logout: () => Promise<void> | void;
  handleCallback: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue>();

export function AuthProvider(props: { children: any }) {
  const settings = getOidcSettings();
  const [user, setUser] = createSignal<User | null>(null);
  const [isLoading, setIsLoading] = createSignal(Boolean(settings));
  const manager = getUserManager();

  const loadUser = async () => {
    if (!manager) {
      setIsLoading(false);
      return;
    }
    setIsLoading(true);
    const currentUser = await manager.getUser();
    setUser(currentUser ?? null);
    setIsLoading(false);
  };

  createEffect(() => {
    void loadUser();
  });

  if (manager) {
    const handleUserLoaded = (loadedUser: User) => {
      setUser(loadedUser);
    };
    const handleUserUnloaded = () => {
      setUser(null);
    };
    manager.events.addUserLoaded(handleUserLoaded);
    manager.events.addUserUnloaded(handleUserUnloaded);
    onCleanup(() => {
      manager.events.removeUserLoaded(handleUserLoaded);
      manager.events.removeUserUnloaded(handleUserUnloaded);
    });
  }

  const value = createMemo<AuthContextValue>(() => ({
    isEnabled: Boolean(settings),
    isLoading: isLoading(),
    isAuthenticated: Boolean(user()),
    user: user(),
    login: () => (manager ? manager.signinRedirect() : undefined),
    logout: () => (manager ? manager.signoutRedirect() : undefined),
    handleCallback: async () => {
      if (!manager) {
        return;
      }
      await manager.signinRedirectCallback();
      await loadUser();
    },
  }));

  return <AuthContext.Provider value={value()}>{props.children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error('AuthContext is not available');
  }
  return ctx;
}
