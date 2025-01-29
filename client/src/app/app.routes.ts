import { Routes } from '@angular/router';
import { signedInGuard } from './core/auth.guard';
import { ChatShellComponent } from './features/chat/chat-shell.component';
import { LoginComponent } from './features/login/login.component';

export const routes: Routes = [
  { path: 'login', component: LoginComponent, title: 'Sign in - Realtime chat' },
  { path: 'chat', component: ChatShellComponent, canActivate: [signedInGuard], title: 'Realtime chat' },
  { path: 'chat/:roomId', component: ChatShellComponent, canActivate: [signedInGuard], title: 'Realtime chat' },
  { path: '', pathMatch: 'full', redirectTo: 'chat' },
  { path: '**', redirectTo: 'chat' },
];
