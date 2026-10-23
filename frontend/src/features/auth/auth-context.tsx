import type { User } from 'oidc-client-ts';
import { createContext, createSignal, onCleanup, onSettled, useContext } from 'solid-js';
import { getOidcSettings, getUserManager } from '../../shared/auth/oidc.ts';

type AuthContextValue = {
  isEnabled: boolean;
  // Solid 2.0: a context value is snapshotted once by its provider, so reactive
  // state must be carried as accessors, not plain fields.
  isLoading: () => boolean;
  isAuthenticated: () => boolean;
  user: () => User | null;
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
  onSettled(() => {
    void loadUser();
  });

  const value: AuthContextValue = {
    isEnabled: Boolean(settings),
    isLoading,
    isAuthenticated: () => Boolean(user()),
    user,
    login: () => (manager ? manager.signinRedirect() : undefined),
    logout: () => (manager ? manager.signoutRedirect() : undefined),
    handleCallback: async () => {
      if (!manager) {
        return;
      }
      await manager.signinRedirectCallback();
      await loadUser();
    }
  };

  return <AuthContext value={value}>{props.children}</AuthContext>;
}

export function useAuth() {
  return useContext(AuthContext);
}
