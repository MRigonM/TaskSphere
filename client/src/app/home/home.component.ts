import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIf } from '@angular/common';
import { AuthStoreService } from '../core/services/auth-store.service';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [RouterLink, NgIf],
  templateUrl: './home.component.html',
})
export class HomeComponent {
  constructor(public auth: AuthStoreService) {}
}
