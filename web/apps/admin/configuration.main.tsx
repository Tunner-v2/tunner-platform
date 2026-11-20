import {createRoot} from 'react-dom/client';
import {ConfigurationApp} from './configuration';
import '@tunner/ui/styles.css';
import './content.css';
import './configuration.css';
createRoot(document.getElementById('root')!).render(<ConfigurationApp/>);
