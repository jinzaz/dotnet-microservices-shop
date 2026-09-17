import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MutationCache, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import { ApiError } from './api';
import { SessionProvider } from './session';
import { ConfirmProvider, ToastProvider } from './ui';
import './styles.css';
import './interaction.css';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Authorization and validation failures will not succeed on retry.
      retry: (failureCount, error) => !(error instanceof ApiError && error.status < 500) && failureCount < 1,
      refetchOnWindowFocus: false,
      staleTime: 15_000,
    },
  },
  mutationCache: new MutationCache({
    onError: (error) => {
      // A session that expired mid-operation cannot recover in place.
      if (error instanceof ApiError && error.status === 401) void queryClient.invalidateQueries({ queryKey: ['bff', 'session'] });
    },
  }),
});
const root = document.getElementById('root');
if (!root) throw new Error('Root element was not found.');

createRoot(root).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <ToastProvider>
          <ConfirmProvider>
            <SessionProvider><App /></SessionProvider>
          </ConfirmProvider>
        </ToastProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
