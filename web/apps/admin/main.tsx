import {createRoot} from 'react-dom/client';
import {ContentApp} from './content';
import '@tunner/ui/styles.css';
import './content.css';
createRoot(document.getElementById('root')!).render(<ContentApp/>);
